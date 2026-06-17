import requests
from mcp.server.fastmcp import FastMCP
from context_engine import ContextEngine

mcp = FastMCP("ZBIM-Copilot-Revit")
REVIT_HTTP_URL = "http://localhost:5000/build/"
OPENTOPO_KEY = "DEMO_KEY" 
context_engine = ContextEngine(api_key_opentopo=OPENTOPO_KEY)

@mcp.tool()
def ejecutar_proyecto_oas(json_oas: str) -> str:
    """
    Ejecuta un proyecto arquitectónico completo en Revit 2027.
    El parámetro json_oas DEBE ser un JSON válido con esta estructura exacta:
    {
      "projectName": "string",
      "typology": "string",
      "site": { "width": 0, "depth": 0, "regulations": { "fos": 0, "fot": 0, "setbacks": { "front": 0, "back": 0, "side": 0 }, "maxHeight": 0 }, "accesses": [] },
      "masterplan": null,
      "buildings": [
        {
          "id": "string", "name": "string", "origin": [0, 0],
          "structuralGrid": null,
          "cores": [ { "id": "string", "type": "string", "origin": [0, 0], "dimensions": [0, 0], "servesLevels": ["string"] } ],
          "levels": [ { "id": "string", "name": "string", "elevation": 0, "f2f": 2800, "use": "string", "zones": [ { "id": "string", "name": "string", "privacyGradient": "string", "fireSector": false, "spaces": [ { "id": "string", "name": "string", "type": "string", "origin": [0, 0], "dimensions": [0, 0], "boundary": "string", "adjacentTo": [] } ] } ] } ]
        }
      ]
    }
    Revit construirá los muros, losas, núcleos y topografía automáticamente.
    """
    try:
        response = requests.post(REVIT_HTTP_URL, data=json_oas.encode('utf-8'), timeout=120)
        if response.status_code == 200:
            return response.text
        else:
            return f"ERROR HTTP {response.status_code}: {response.text}"
    except requests.exceptions.ConnectionError:
        return "ERROR: No se pudo conectar a Revit. Asegúrese de que esté abierto."
    except Exception as e:
        return f"ERROR INESPERADO: {str(e)}"

@mcp.tool()
def consultar_contexto_sitio(latitud: float, longitud: float) -> str:
    """
    Consulta datos climáticos (viento dominante, radiación solar) para una ubicación.
    Retorna un resumen para que la IA pueda tomar decisiones de diseño.
    """
    data = context_engine.fetch_climate(latitude=latitud, longitude=longitud)
    if data:
        return f"Contexto en Lat:{latitud}, Lon:{longitud}. Viento dominante: {data['dominant_wind_direction_100m']}°. Radiación: {data['avg_direct_normal_irradiance_w_m2']} W/m2."
    return "No se pudo obtener el contexto del sitio."

if __name__ == "__main__":
    mcp.run()