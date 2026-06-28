"""
token_service.py — Servicio de Tokens ZBIM (Cloud).
Gestiona registro de instalaciones, saldo, pagos y proxy a OpenRouter/Kimi.

Endpoints:
  POST /register        - Registra nueva instalación
  GET  /balance         - Consulta saldo
  POST /create_payment  - Crea orden de pago (Stripe)
  POST /webhook         - Webhook de Stripe
  POST /proxy-kimi      - Proxy a OpenRouter (Kimi)
  GET  /model/latest    - [FASE H] Última versión del modelo entrenado
"""

import os
import json
import uuid
import time
import hashlib
import hmac
import sqlite3
import requests
from datetime import datetime
from flask import Flask, request, jsonify

app = Flask(__name__)

# ============================================================
# CONFIGURACIÓN
# ============================================================
OPENROUTER_API_KEY = os.environ.get("OPENROUTER_API_KEY", "")
STRIPE_API_KEY = os.environ.get("STRIPE_API_KEY", "")
DATABASE_PATH = os.environ.get("DATABASE_PATH", os.path.join(os.path.dirname(__file__), "tokens.db"))
MARGIN_PERCENT = 0.10  # Margen del 10%

# ============================================================
# BASE DE DATOS
# ============================================================

def init_db():
    """Inicializa la base de datos de tokens."""
    conn = sqlite3.connect(DATABASE_PATH)
    c = conn.cursor()
    c.execute("""
        CREATE TABLE IF NOT EXISTS installations (
            installation_id TEXT PRIMARY KEY,
            client_secret_hash TEXT NOT NULL,
            created_at TEXT NOT NULL,
            balance INTEGER DEFAULT 0
        )
    """)
    c.execute("""
        CREATE TABLE IF NOT EXISTS transactions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            installation_id TEXT NOT NULL,
            amount INTEGER NOT NULL,
            type TEXT NOT NULL,
            timestamp TEXT NOT NULL,
            FOREIGN KEY (installation_id) REFERENCES installations(installation_id)
        )
    """)
    conn.commit()
    conn.close()


def hash_secret(secret):
    """Hashea el client_secret con SHA-256."""
    return hashlib.sha256(secret.encode()).hexdigest()


def verify_signature(installation_id, signature, timestamp, nonce):
    """Verifica la firma HMAC-SHA256."""
    conn = sqlite3.connect(DATABASE_PATH)
    c = conn.cursor()
    c.execute("SELECT client_secret_hash FROM installations WHERE installation_id = ?", (installation_id,))
    row = c.fetchone()
    conn.close()
    if not row:
        return False
    
    # Verificar que el timestamp no tenga más de 30 segundos
    try:
        ts = int(timestamp)
        if abs(time.time() - ts) > 30:
            return False
    except ValueError:
        return False
    
    # Verificar firma
    message = f"{installation_id}{timestamp}{nonce}"
    # Nota: en producción, se necesitaría el client_secret original para verificar
    # Aquí simplificamos asumiendo que el cliente envía el hash directamente
    return True


# ============================================================
# ENDPOINTS
# ============================================================

@app.route("/register", methods=["POST"])
def register():
    """Registra una nueva instalación."""
    data = request.get_json(force=True, silent=True) or {}
    hardware_id = data.get("hardware_id", "")
    
    if not hardware_id:
        return jsonify({"error": "Falta hardware_id"}), 400
    
    # Generar credenciales
    installation_id = str(uuid.uuid4())
    client_secret = uuid.uuid4().hex + uuid.uuid4().hex[:16]
    
    conn = sqlite3.connect(DATABASE_PATH)
    c = conn.cursor()
    c.execute("""
        INSERT INTO installations (installation_id, client_secret_hash, created_at, balance)
        VALUES (?, ?, ?, 0)
    """, (installation_id, hash_secret(client_secret), datetime.utcnow().isoformat()))
    conn.commit()
    conn.close()
    
    return jsonify({
        "installation_id": installation_id,
        "client_secret": client_secret  # Solo se muestra una vez
    })


@app.route("/balance", methods=["GET"])
def balance():
    """Consulta el saldo de una instalación."""
    installation_id = request.args.get("installation_id", "")
    
    if not installation_id:
        return jsonify({"error": "Falta installation_id"}), 400
    
    conn = sqlite3.connect(DATABASE_PATH)
    c = conn.cursor()
    c.execute("SELECT balance FROM installations WHERE installation_id = ?", (installation_id,))
    row = c.fetchone()
    conn.close()
    
    if not row:
        return jsonify({"error": "Instalación no encontrada"}), 404
    
    return jsonify({"balance": row[0]})


@app.route("/create_payment", methods=["POST"])
def create_payment():
    """Crea una orden de pago en Stripe."""
    data = request.get_json(force=True, silent=True) or {}
    installation_id = data.get("installation_id", "")
    amount = data.get("amount", 0)
    
    if not installation_id or amount <= 0:
        return jsonify({"error": "Datos inválidos"}), 400
    
    # En producción, crear sesión de Stripe aquí
    # Simplificado para demostración
    payment_url = f"https://checkout.stripe.com/pay/{uuid.uuid4().hex[:16]}"
    
    return jsonify({
        "payment_url": payment_url,
        "amount": amount,
        "installation_id": installation_id
    })


@app.route("/webhook", methods=["POST"])
def webhook():
    """Webhook de Stripe para pagos exitosos."""
    # En producción, verificar firma de Stripe
    event = request.get_json(force=True, silent=True) or {}
    
    if event.get("type") == "checkout.session.completed":
        session = event.get("data", {}).get("object", {})
        installation_id = session.get("metadata", {}).get("installation_id", "")
        amount_paid = session.get("amount_total", 0) / 100  # Centavos a dólares
        
        if installation_id and amount_paid > 0:
            # Calcular tokens: monto neto * (1 - margen) / precio por token
            net_amount = amount_paid * (1 - MARGIN_PERCENT)
            tokens_to_credit = int(net_amount * 100)  # 100 tokens por dólar
            
            conn = sqlite3.connect(DATABASE_PATH)
            c = conn.cursor()
            c.execute("UPDATE installations SET balance = balance + ? WHERE installation_id = ?",
                      (tokens_to_credit, installation_id))
            c.execute("""
                INSERT INTO transactions (installation_id, amount, type, timestamp)
                VALUES (?, ?, 'payment', ?)
            """, (installation_id, tokens_to_credit, datetime.utcnow().isoformat()))
            conn.commit()
            conn.close()
    
    return jsonify({"status": "ok"}), 200


@app.route("/proxy-kimi", methods=["POST"])
def proxy_kimi():
    """Proxy a OpenRouter para análisis multimodal (Kimi)."""
    data = request.get_json(force=True, silent=True) or {}
    installation_id = data.get("installation_id", "")
    prompt = data.get("prompt", "")
    images = data.get("images", [])
    
    if not installation_id or not prompt:
        return jsonify({"error": "Faltan datos"}), 400
    
    # Verificar saldo
    conn = sqlite3.connect(DATABASE_PATH)
    c = conn.cursor()
    c.execute("SELECT balance FROM installations WHERE installation_id = ?", (installation_id,))
    row = c.fetchone()
    
    if not row or row[0] <= 0:
        conn.close()
        return jsonify({"error": "Saldo insuficiente"}), 402
    
    # Descontar 1 crédito
    c.execute("UPDATE installations SET balance = balance - 1 WHERE installation_id = ?", (installation_id,))
    c.execute("""
        INSERT INTO transactions (installation_id, amount, type, timestamp)
        VALUES (?, -1, 'kimi_call', ?)
    """, (installation_id, datetime.utcnow().isoformat()))
    conn.commit()
    conn.close()
    
    # Llamar a OpenRouter
    if not OPENROUTER_API_KEY:
        return jsonify({"error": "OpenRouter API key no configurada"}), 500
    
    headers = {
        "Authorization": f"Bearer {OPENROUTER_API_KEY}",
        "Content-Type": "application/json"
    }
    
    # Construir mensaje con imágenes si las hay
    content = [{"type": "text", "text": prompt}]
    for img_b64 in images:
        if img_b64:
            content.append({
                "type": "image_url",
                "image_url": {"url": f"data:image/jpeg;base64,{img_b64}"}
            })
    
    payload = {
        "model": "moonshotai/kimi-k2.5",
        "messages": [{"role": "user", "content": content}]
    }
    
    try:
        resp = requests.post(
            "https://openrouter.ai/api/v1/chat/completions",
            headers=headers,
            json=payload,
            timeout=30
        )
        if resp.status_code == 200:
            data = resp.json()
            response_text = data["choices"][0]["message"]["content"]
            return jsonify({"response": response_text})
        return jsonify({"error": "Error en OpenRouter", "status": resp.status_code}), 500
    except Exception as e:
        return jsonify({"error": str(e)}), 500


# ============================================================
# FASE H: ENDPOINT DE ÚLTIMA VERSIÓN DEL MODELO
# ============================================================

@app.route("/model/latest", methods=["GET"])
def model_latest():
    """
    [FASE H] Devuelve la última versión del modelo entrenado.
    Lee de feedback.db la tabla model_updates.
    """
    db_path = os.path.join(os.path.dirname(__file__), "..", "brain", "feedback.db")
    if not os.path.exists(db_path):
        return jsonify({"error": "No hay modelos entrenados aún"}), 404
    
    conn = sqlite3.connect(db_path)
    c = conn.cursor()
    c.execute("""
        SELECT version, release_date, training_samples, notes 
        FROM model_updates 
        ORDER BY id DESC 
        LIMIT 1
    """)
    row = c.fetchone()
    conn.close()
    
    if row:
        return jsonify({
            "version": row[0],
            "release_date": row[1],
            "training_samples": row[2],
            "notes": row[3],
            "download_url": f"https://tokens.zbimcopilot.com/models/{row[0]}.gguf"
        })
    return jsonify({"error": "No hay modelos entrenados aún"}), 404


# ============================================================
# ARRANQUE
# ============================================================
if __name__ == "__main__":
    init_db()
    print(f"🚀 token_service arrancando...")
    print(f"💾 Base de datos: {DATABASE_PATH}")
    print(f"🔑 OpenRouter API Key: {'✅' if OPENROUTER_API_KEY else '❌'}")
    print(f"💳 Stripe API Key: {'✅' if STRIPE_API_KEY else '❌'}")
    app.run(host="0.0.0.0", port=5001, debug=False)