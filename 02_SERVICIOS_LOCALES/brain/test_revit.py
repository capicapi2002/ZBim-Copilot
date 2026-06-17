import requests
import json

REVIT_API_URL = "http://localhost:8080/oas/"
JSON_FILE = "casa_completa.json"

def main():
    print(f"📤 Leyendo archivo: {JSON_FILE}...")
    try:
        with open(JSON_FILE, 'r', encoding='utf-8') as f:
            payload = json.load(f)
    except FileNotFoundError:
        print(f"❌ ERROR: No se encontró '{JSON_FILE}'. Ejecute primero generar_casa.py")
        return

    print(f"📡 Enviando proyecto '{payload.get('ProjectName')}' a Revit...")
    print("⏳ Espere a que Revit procese la geometría...")

    try:
        response = requests.post(REVIT_API_URL, json=payload, timeout=45)
        if response.status_code == 200:
            print(f"✅ ¡ÉXITO! Revit respondió: {response.status_code} - {response.text}")
        else:
            print(f"⚠️ Revit respondió con código {response.status_code}: {response.text}")
    except requests.exceptions.Timeout:
        print("⏱️ TIMEOUT: Revit tardó más de 45 segundos. Revise el panel Odysseus en Revit.")
    except requests.exceptions.ConnectionError:
        print("❌ ERROR DE CONEXIÓN: Asegúrese de que Revit esté abierto y el Solver ejecutado.")
    except Exception as e:
        print(f"❌ ERROR: {e}")

if __name__ == "__main__":
    main()