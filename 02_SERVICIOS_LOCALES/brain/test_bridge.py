import requests

print("--- PROBANDO PUENTE HTTP HACIA REVIT ---")
try:
    r = requests.post("http://localhost:5000/build/", data='{"typology":"TestHTTP"}', timeout=15)
    print(f"Estado HTTP: {r.status_code}")
    print(f"Respuesta de Revit: {r.text}")
except requests.exceptions.ConnectionError:
    print("ERROR: No se pudo conectar a Revit. Asegurese de que Revit este abierto y el Add-in cargado.")
except Exception as e:
    print(f"Error inesperado: {e}")
