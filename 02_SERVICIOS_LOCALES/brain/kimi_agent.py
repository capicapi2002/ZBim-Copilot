import json
import requests
import os
from openai import OpenAI
from typing import Optional, Dict, Tuple

# ========== CONFIGURACIÓN ARQUITECTÓNICA ZBIM ==========
REVIT_HTTP_URL = "http://localhost:8080/" # URL RAÍZ SIMPLIFICADA
MAX_RETRIES = 3

OPENROUTER_API_KEY = "open ruter key aquí"
MODEL_ID = "moonshotai/kimi-k2.5" # Slug de Kimi K2.5 en OpenRouter

client = OpenAI(
    base_url="https://openrouter.ai/api/v1",
    api_key=OPENROUTER_API_KEY
)

# ========== HERRAMIENTA LLM ==========
tools = [
    {
        "type": "function",
        "function": {
            "name": "ejecutar_proyecto_oas",
            "description": "Envía el JSON-OAS estricto y validado al motor Text2MBL de Revit.",
            "parameters": {
                "type": "object",
                "properties": {
                    "json_oas": {
                        "type": "string",
                        "description": "Cadena de texto JSON-OAS válida, completa y estricta."
                    }
                },
                "required": ["json_oas"]
            }
        }
    }
]

# ========== FUNCIONES AUXILIARES ==========
def validate_json_schema(oas_str: str) -> Tuple[bool, Optional[Dict], str]:
    try:
        data = json.loads(oas_str)
    except json.JSONDecodeError as e:
        return False, None, f"JSON invalido: {e}"
    
    if not isinstance(data, dict):
        return False, None, "El JSON no es un objeto."
    if "projectName" not in data or not data["projectName"]:
        return False, None, "Falta 'projectName' o esta vacio."
    if "buildings" not in data or not isinstance(data["buildings"], list) or len(data["buildings"]) == 0:
        return False, None, "Falta 'buildings' o es una lista vacia."
    
    for bld in data["buildings"]:
        if "structuralGrid" in bld and bld["structuralGrid"] == {}:
            bld["structuralGrid"] = None
            
        if "levels" not in bld or not isinstance(bld["levels"], list) or len(bld["levels"]) == 0:
            return False, None, f"Edificio {bld.get('id', '?')} sin niveles."
        for lvl in bld["levels"]:
            if "zones" not in lvl or not isinstance(lvl["zones"], list) or len(lvl["zones"]) == 0:
                return False, None, f"Nivel {lvl.get('id', '?')} sin zonas."
                
    return True, data, ""

def request_oas_from_llm(user_prompt: str, system_prompt: str) -> Optional[str]:
    messages = [
        {"role": "system", "content": system_prompt},
        {"role": "user", "content": user_prompt}
    ]
    
    response = client.chat.completions.create(
        model=MODEL_ID,
        messages=messages,
        tools=tools,
        tool_choice={"type": "function", "function": {"name": "ejecutar_proyecto_oas"}},
        temperature=0.1,
        max_tokens=8000
    )
    
    message = response.choices[0].message
    if not message.tool_calls:
        print("⚠️ El LLM no invoco la herramienta.")
        return None
    
    tool_call = message.tool_calls[0]
    if tool_call.function.name != "ejecutar_proyecto_oas":
        print("⚠️ Herramienta incorrecta.")
        return None
    
    try:
        args = json.loads(tool_call.function.arguments)
        raw_json = args.get("json_oas")
        if not raw_json:
            print("⚠️ El argumento 'json_oas' esta vacio o no existe.")
            return None
        if isinstance(raw_json, dict):
            raw_json = json.dumps(raw_json, ensure_ascii=False)
        return str(raw_json)
    except json.JSONDecodeError:
        return tool_call.function.arguments

def send_to_revit(payload: str) -> Tuple[bool, str]:
    try:
        resp = requests.post(
            REVIT_HTTP_URL,
            data=payload.encode('utf-8'),
            timeout=120,
            headers={'Content-Type': 'application/json'}
        )
        if resp.status_code == 200:
            return True, resp.text
        else:
            return False, f"HTTP {resp.status_code}: {resp.text}"
    except Exception as e:
        return False, f"Error de conexion: {e}"

# ========== LOGICA PRINCIPAL ==========
def run_agent(user_prompt: str, system_prompt: str):
    attempt = 0
    
    while attempt < MAX_RETRIES:
        attempt += 1
        print(f"\n🔄 Intento {attempt} de generacion OAS...")
        
        oas_json = request_oas_from_llm(user_prompt, system_prompt)
        if not oas_json:
            print("❌ No se recibio JSON del LLM.")
            continue
        
        valid, _, error_msg = validate_json_schema(oas_json)
        if valid:
            print("✅ JSON validado contra esquema C#. Enviando a Revit...")
            success, revit_msg = send_to_revit(oas_json)
            if success:
                print(f"🎉 Revit proceso correctamente: {revit_msg}")
                return
            else:
                print(f"❌ Revit rechazo el JSON: {revit_msg}")
                return
        else:
            print(f"⚠️ JSON invalido: {error_msg}")
            print("❌ Error estructural, reintentando generacion completa...")
    
    print("💥 Se agotaron los reintentos. No se pudo generar un JSON valido.")

# ========== EJECUCION ==========
if __name__ == "__main__":
    SYSTEM_PROMPT = """Eres el Motor de Sintesis Arquitectonica de ZBIM-Copilot. Tu UNICA funcion es generar un JSON-OAS perfecto que pueda ser deserializado directamente por System.Text.Json en C# (.NET 10).

REGLAS DE ORO INAMOVIBLES (SI ROMPES UNA, REVIT CRASHEA):
1. SCHEMA C# ESTRICTO: El JSON debe coincidir EXACTAMENTE con estos records de C#. No inventes campos, no cambies los tipos de datos:
   - OasProject (RAIZ): projectName (string), buildings (List<OasBuilding>)
   - OasBuilding: id (string), name (string), origin (double[]), structuralGrid (OasStructuralGrid? NULLABLE), cores (List<OasCore>), levels (List<OasLevel>)
   - OasStructuralGrid: xSpans (List<double>? NULLABLE), ySpans (List<double>? NULLABLE)
   - OasCore: id, type, origin, dimensions, servesLevels (string[])
   - OasLevel: id, name, elevation (double), f2f (double), use, zones (List<OasZone>)
   - OasZone: id, name, privacyGradient, fireSector (bool), spaces (List<OasSpace>)
   - OasSpace: id, name, type, origin (double[]), dimensions (double[]), boundary, adjacentTo (string[])

2. NULIDAD: Si una vivienda no usa reticula estructural, "structuralGrid" DEBE SER `null`. NO ENVIES un objeto vacio `{}`.
3. LISTAS: Todas las listas (cores, levels, zones, spaces) deben contener al menos 1 elemento. No mandes listas vacias `[]`.
4. GEOMETRIA REAL: origin = [X, Y, Z], dimensions = [Ancho, Profundidad]. Planta Baja empieza en origin [0,0,0]. Planta Alta en [0,0,2800]. Calcula matematicamente el origin de cada espacio sumando dimensiones para que sean adyacentes y NO se superpongan.
5. SINTAXIS JSON PURA: CERO comas finales. CERO comillas simples. CERO texto fuera del JSON. JSON COMPLETO, no lo cortes.
"""
    
    USER_PROMPT = "Genera una vivienda pareada de 15x35m. Planta Baja: Living 4x5m, Cocina 3x3m, Baño 2x2m, Distribuidor 2x3m. Planta Alta: Dormitorio 1 4x4m, Dormitorio 2 3x4m, Baño 2x2m, Pasillo 1x4m. F2F 2800mm. Medianerías STC 55."
    
    run_agent(USER_PROMPT, SYSTEM_PROMPT)