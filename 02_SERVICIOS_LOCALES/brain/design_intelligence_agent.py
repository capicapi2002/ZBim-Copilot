"""
design_intelligence_agent.py — Microservicio local de inteligencia de diseño para ZBIM-Copilot.
Arranque: python design_intelligence_agent.py (Flask en 127.0.0.1:5000)

Endpoints:
  GET  /health           - Health check
  POST /generate_oas     - Genera OAS desde FullProjectConfig (Ollama + Kimi opcional)
  POST /save_normative   - Guarda normativa en normatives/
  GET  /list_normatives  - Lista normativas guardadas
  GET  /token_balance    - Obtiene saldo de tokens del usuario
  POST /init_recharge    - Inicia recarga de tokens
  POST /feedback         - [FASE H] Recibe feedback anónimo de modificaciones del OAS
  POST /enrich_context   - [FASE E] Obtiene datos de topografía y clima para una ubicación
"""

import os
import json
import re
import base64
import time
import hashlib
import hmac
import requests
import sqlite3
from datetime import datetime
from flask import Flask, request, jsonify

app = Flask(__name__)

# ============================================================
# CONFIGURACIÓN
# ============================================================
OLLAMA_BASE_URL = os.environ.get("OLLAMA_BASE_URL", "http://localhost:11434")
OLLAMA_MODEL = os.environ.get("OLLAMA_MODEL", "gemma:7b")
TOKEN_SERVICE_URL = os.environ.get("TOKEN_SERVICE_URL", "https://tokens.zbimcopilot.com")
INSTALLATION_ID_PATH = os.environ.get(
    "INSTALLATION_ID_PATH",
    os.path.join(os.path.dirname(__file__), "..", "credentials.json")
)

FEEDBACK_DB_PATH = os.path.join(os.path.dirname(__file__), "feedback.db")

# ============================================================
# FUNCIONES AUXILIARES
# ============================================================

def load_installation_credentials():
    """Carga installation_id y client_secret desde el archivo JSON."""
    try:
        if os.path.exists(INSTALLATION_ID_PATH):
            with open(INSTALLATION_ID_PATH, "r") as f:
                return json.load(f)
    except Exception:
        pass
    return {"installation_id": "", "client_secret": ""}


def call_ollama(prompt, model=None, timeout=60):
    """Llama a Ollama para generar texto."""
    model = model or OLLAMA_MODEL
    url = f"{OLLAMA_BASE_URL}/api/generate"
    payload = {
        "model": model,
        "prompt": prompt,
        "stream": False,
        "options": {"temperature": 0.3, "num_predict": 4096}
    }
    try:
        resp = requests.post(url, json=payload, timeout=timeout)
        if resp.status_code == 200:
            return resp.json().get("response", "")
    except Exception as e:
        print(f"⚠️ Error llamando a Ollama: {e}")
    return None


def extract_json_from_text(text):
    """Extrae el primer bloque JSON válido de un texto."""
    # Intentar encontrar bloque entre { y }
    match = re.search(r'\{[\s\S]*\}', text)
    if match:
        try:
            return json.loads(match.group())
        except json.JSONDecodeError:
            pass
    # Intentar parsear todo el texto
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return None


def build_oas_prompt(config):
    """Construye un prompt detallado para generar el OAS."""
    project_name = config.get("project_name", "Proyecto")
    project_type = config.get("project_type", "Vivienda")
    location = config.get("location", "España")
    floors_above = config.get("floors_above", 1)
    floors_below = config.get("floors_below", 0)
    program_text = config.get("program_text", "")
    implantation_area = config.get("implantation_area", 0)
    retreats = config.get("retreats", {})
    style = config.get("style_references", {}).get("style", "")
    climate = config.get("style_references", {}).get("climate", "")
    max_height = config.get("style_references", {}).get("max_height", 0)
    materials = config.get("style_references", {}).get("materials", [])

    prompt = f"""Eres un arquitecto experto en diseño BIM. Genera un OAS (Open Architectural Schema) en JSON válido para el siguiente proyecto:

Nombre: {project_name}
Tipo: {project_type}
Ubicación: {location}
Plantas sobre rasante: {floors_above}
Plantas bajo rasante: {floors_below}
Área de implantación: {implantation_area} m²
Retiros: frente={retreats.get('front',0)}m, lado={retreats.get('side',0)}m, fondo={retreats.get('back',0)}m
Estilo: {style}
Clima: {climate}
Altura máxima: {max_height}m
Materiales: {', '.join(materials) if materials else 'No especificados'}

Programa de necesidades:
{program_text}

Genera un JSON con la siguiente estructura:
{{
  "spaces": [
    {{
      "name": "Nombre del espacio",
      "function": "función",
      "area": número en m²,
      "dimensions": {{"width": x, "depth": y}},
      "level": número de planta (0 = planta baja),
      "adjacencies": ["lista de espacios adyacentes"],
      "notes": "descripción opcional"
    }}
  ],
  "levels": [
    {{"id": 0, "name": "Planta Baja", "elevation": 0.0}}
  ],
  "total_area": número total en m²,
  "source": "ollama"
}}

Responde SOLO con el JSON válido, sin texto adicional."""

    return prompt


# ============================================================
# ENDPOINTS EXISTENTES
# ============================================================

@app.route("/health", methods=["GET"])
def health():
    """Health check."""
    return jsonify({"status": "ok", "service": "design_intelligence_agent", "timestamp": datetime.utcnow().isoformat()})


@app.route("/generate_oas", methods=["POST"])
def generate_oas():
    """
    Genera OAS desde FullProjectConfig.
    Paso 1: Ollama (obligatorio, local)
    Paso 2: Kimi (opcional, si hay saldo) para análisis multimodal
    """
    config = request.get_json(force=True, silent=True) or {}

    # Paso 1: Ollama
    prompt = build_oas_prompt(config)
    ollama_response = call_ollama(prompt, timeout=30)

    if ollama_response is None:
        return jsonify({"error": "Ollama no responde. Verifica que el servicio está activo."}), 503

    oas = extract_json_from_text(ollama_response)
    if oas is None:
        # Reintento con prompt más estricto
        strict_prompt = prompt + "\n\nIMPORTANTE: Responde ÚNICAMENTE con JSON válido. No incluyas texto, markdown ni explicaciones."
        ollama_response = call_ollama(strict_prompt, timeout=30)
        if ollama_response:
            oas = extract_json_from_text(ollama_response)

    if oas is None:
        return jsonify({"error": "No se pudo generar un OAS JSON válido.", "raw": ollama_response[:500]}), 500

    source = "ollama"

    # Paso 2: Kimi (si hay saldo)
    try:
        creds = load_installation_credentials()
        if creds.get("installation_id") and creds.get("client_secret"):
            # Verificar saldo
            balance_resp = requests.get(
                f"{TOKEN_SERVICE_URL}/balance",
                params={"installation_id": creds["installation_id"]},
                timeout=5
            )
            if balance_resp.status_code == 200:
                balance = balance_resp.json().get("balance", 0)
                if balance > 0:
                    # Enviar imágenes a Kimi si las hay
                    images = config.get("images_references", [])
                    if images:
                        kimi_payload = {
                            "installation_id": creds["installation_id"],
                            "prompt": "Describe brevemente estas imágenes de referencia arquitectónica para enriquecer un OAS.",
                            "images": [img.get("data", "") for img in images if img.get("data")]
                        }
                        kimi_resp = requests.post(
                            f"{TOKEN_SERVICE_URL}/proxy-kimi",
                            json=kimi_payload,
                            timeout=30
                        )
                        if kimi_resp.status_code == 200:
                            kimi_data = kimi_resp.json()
                            description = kimi_data.get("response", "")
                            if description:
                                # Añadir notas al OAS
                                for space in oas.get("spaces", []):
                                    if "notes" not in space or not space["notes"]:
                                        space["notes"] = f"Ref: {description[:100]}"
                                source = "ollama+kimi"
    except Exception as e:
        print(f"⚠️ Error en paso Kimi: {e}")

    oas["source"] = source
    return jsonify(oas)


@app.route("/save_normative", methods=["POST"])
def save_normative():
    """Guarda un archivo de normativa en normatives/."""
    data = request.get_json(force=True, silent=True) or {}
    name = data.get("name", "")
    content_b64 = data.get("content_base64", "")

    if not name or not content_b64:
        return jsonify({"error": "Faltan campos 'name' o 'content_base64'"}), 400

    normatives_dir = os.path.join(os.path.dirname(__file__), "normatives")
    os.makedirs(normatives_dir, exist_ok=True)

    try:
        content = base64.b64decode(content_b64)
        filepath = os.path.join(normatives_dir, name)
        with open(filepath, "wb") as f:
            f.write(content)
        return jsonify({"path": f"normatives/{name}"})
    except Exception as e:
        return jsonify({"error": str(e)}), 500


@app.route("/list_normatives", methods=["GET"])
def list_normatives():
    """Lista las normativas guardadas."""
    normatives_dir = os.path.join(os.path.dirname(__file__), "normatives")
    if not os.path.exists(normatives_dir):
        return jsonify({"normatives": []})

    normatives = []
    for fname in os.listdir(normatives_dir):
        fpath = os.path.join(normatives_dir, fname)
        if os.path.isfile(fpath):
            normatives.append({
                "name": fname,
                "path": f"normatives/{fname}",
                "size": os.path.getsize(fpath)
            })
    return jsonify({"normatives": normatives})


@app.route("/token_balance", methods=["GET"])
def token_balance():
    """Obtiene el saldo de tokens del usuario."""
    creds = load_installation_credentials()
    if not creds.get("installation_id"):
        return jsonify({"error": "No hay credenciales de instalación"}), 401

    try:
        resp = requests.get(
            f"{TOKEN_SERVICE_URL}/balance",
            params={"installation_id": creds["installation_id"]},
            timeout=5
        )
        if resp.status_code == 200:
            return jsonify(resp.json())
        return jsonify({"error": "Error obteniendo saldo"}), resp.status_code
    except Exception as e:
        return jsonify({"error": str(e)}), 500


@app.route("/init_recharge", methods=["POST"])
def init_recharge():
    """Inicia una recarga de tokens."""
    data = request.get_json(force=True, silent=True) or {}
    amount = data.get("amount", 0)
    if amount <= 0:
        return jsonify({"error": "Monto inválido"}), 400

    creds = load_installation_credentials()
    if not creds.get("installation_id"):
        return jsonify({"error": "No hay credenciales de instalación"}), 401

    try:
        resp = requests.post(
            f"{TOKEN_SERVICE_URL}/create_payment",
            json={
                "installation_id": creds["installation_id"],
                "amount": amount
            },
            timeout=10
        )
        if resp.status_code == 200:
            return jsonify(resp.json())
        return jsonify({"error": "Error iniciando recarga"}), resp.status_code
    except Exception as e:
        return jsonify({"error": str(e)}), 500


# ============================================================
# FASE H: SISTEMA DE FEEDBACK Y APRENDIZAJE CONTINUO
# ============================================================

def init_feedback_db():
    """Inicializa la base de datos de feedback si no existe."""
    conn = sqlite3.connect(FEEDBACK_DB_PATH)
    c = conn.cursor()
    c.execute("""
        CREATE TABLE IF NOT EXISTS project_deltas (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp TEXT NOT NULL,
            installation_id TEXT NOT NULL,
            project_type TEXT,
            location TEXT,
            floors_above INTEGER,
            implantation_area REAL,
            original_spaces_json TEXT NOT NULL,
            modified_spaces_json TEXT NOT NULL,
            delta_json TEXT NOT NULL
        )
    """)
    c.execute("""
        CREATE TABLE IF NOT EXISTS model_updates (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            version TEXT NOT NULL,
            release_date TEXT NOT NULL,
            training_samples INTEGER,
            notes TEXT
        )
    """)
    conn.commit()
    conn.close()


def calculate_delta(original: dict, modified: dict) -> dict:
    """Calcula las diferencias entre el OAS original y el modificado."""
    delta = {"added": [], "removed": [], "modified": []}
    orig_spaces = {s["name"]: s for s in original.get("spaces", [])}
    mod_spaces = {s["name"]: s for s in modified.get("spaces", [])}

    # Espacios añadidos o modificados
    for name, s in mod_spaces.items():
        if name not in orig_spaces:
            delta["added"].append(s)
        else:
            o = orig_spaces[name]
            if abs(o.get("area", 0) - s.get("area", 0)) > 0.01:
                delta["modified"].append({
                    "name": name,
                    "original_area": o.get("area"),
                    "modified_area": s.get("area")
                })

    # Espacios eliminados
    for name in orig_spaces:
        if name not in mod_spaces:
            delta["removed"].append({"name": name})

    return delta


@app.route("/feedback", methods=["POST"])
def feedback():
    """
    [FASE H] Recibe feedback anónimo de modificaciones del OAS.
    Almacena deltas para reentrenamiento futuro del modelo.
    """
    data = request.get_json(force=True, silent=True) or {}
    installation_id = data.get("installation_id", "")
    config = data.get("config", {})
    original_oas = data.get("original_oas", {})
    modified_oas = data.get("modified_oas", {})

    if not installation_id or not original_oas or not modified_oas:
        return jsonify({"error": "Faltan campos obligatorios"}), 400

    try:
        delta = calculate_delta(original_oas, modified_oas)
        conn = sqlite3.connect(FEEDBACK_DB_PATH)
        c = conn.cursor()
        c.execute("""
            INSERT INTO project_deltas 
            (timestamp, installation_id, project_type, location, floors_above, implantation_area,
             original_spaces_json, modified_spaces_json, delta_json)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        """, (
            datetime.utcnow().isoformat(),
            installation_id,
            config.get("project_type", ""),
            config.get("location", ""),
            config.get("floors_above", 0),
            config.get("implantation_area", 0),
            json.dumps(original_oas),
            json.dumps(modified_oas),
            json.dumps(delta)
        ))
        conn.commit()
        conn.close()
        return jsonify({"status": "ok", "id": c.lastrowid})
    except Exception as e:
        return jsonify({"error": str(e)}), 500


# ============================================================
# FASE H: ACTUALIZACIÓN AUTOMÁTICA DE MODELO
# ============================================================

def check_model_update():
    """Verifica si hay una nueva versión del modelo disponible."""
    try:
        creds = load_installation_credentials()
        if not creds.get("installation_id"):
            return
        resp = requests.get(f"{TOKEN_SERVICE_URL}/model/latest", timeout=5)
        if resp.status_code == 200:
            data = resp.json()
            latest_version = data.get("version")
            current_version = os.environ.get("ZBIM_MODEL_VERSION", "0")
            if latest_version and latest_version != current_version:
                print(f"🔔 Nueva versión del modelo disponible: {latest_version}")
                # La descarga e instalación se haría aquí con ollama create
    except Exception:
        pass


# ============================================================
# FASE E: ENDPOINT DE ENRIQUECIMIENTO DE CONTEXTO
# ============================================================

@app.route("/enrich_context", methods=["POST"])
def enrich_context():
    """
    [FASE E] Recibe una ubicación (texto o coordenadas), radio y opcionalmente un KML
    con el polígono de la parcela. Devuelve datos de topografía, clima y contorno.
    """
    data = request.get_json(force=True, silent=True) or {}
    location = data.get("location", "")
    latitude = data.get("latitude")
    longitude = data.get("longitude")
    radius = data.get("radius", 200)
    kml = data.get("kml", "")                    # <--- NUEVO: recogemos el KML
    
    if not location and (latitude is None or longitude is None):
        return jsonify({"error": "Se requiere 'location' o 'latitude'+'longitude'"}), 400
    
    try:
        from context_engine import ContextEngine
        api_key = os.environ.get("OPENTOPO_API_KEY", "9c89a797b18ede702687422b4974baa1")
        engine = ContextEngine(api_key_opentopo=api_key)
        
        context = engine.enrich_context(
            location=location if location else None,
            latitude=latitude,
            longitude=longitude,
            radius_m=radius,
            kml_string=kml if kml else None       # <--- NUEVO: pasamos el KML
        )
        return jsonify(context)
    except Exception as e:
        return jsonify({"error": str(e)}), 500


# ============================================================
# ARRANQUE
# ============================================================
if __name__ == "__main__":
    init_feedback_db()
    check_model_update()
    print(f"🚀 design_intelligence_agent arrancando en http://127.0.0.1:5000")
    print(f"📦 Ollama: {OLLAMA_BASE_URL} (modelo: {OLLAMA_MODEL})")
    print(f"🔑 Token Service: {TOKEN_SERVICE_URL}")
    print(f"💾 Feedback DB: {FEEDBACK_DB_PATH}")
    app.run(host="127.0.0.1", port=5000, debug=False)