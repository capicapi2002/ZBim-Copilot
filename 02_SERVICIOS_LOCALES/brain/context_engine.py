import requests
import rasterio
import numpy as np
import os
import json

class ContextEngine:
    def __init__(self, api_key_opentopo):
        self.api_key = api_key_opentopo

    def fetch_topography(self, south, north, west, east, output_dir="context_output"):
        print(f"🗺️ Descargando topografía: S:{south} N:{north} W:{west} E:{east}")
        
        os.makedirs(output_dir, exist_ok=True)
        tiff_path = os.path.join(output_dir, "terrain.tif")
        csv_path = os.path.join(output_dir, "terrain_revit.csv")

        # CORRECCIÓN DEFINITIVA: Usar diccionario de parámetros con 'demtype' sin guion
        base_url = "https://portal.opentopography.org/API/globaldem"
        params = {
            "demtype": "SRTMGL1",  # Parámetro correcto
            "south": south,
            "north": north,
            "west": west,
            "east": east,
            "outputFormat": "GTiff",
            "API_Key": self.api_key
        }

        try:
            print("🌐 Conectando a OpenTopography...")
            response = requests.get(base_url, params=params, stream=True)
            
            if response.status_code == 200:
                content_type = response.headers.get('content-type', '')
                if 'image/tiff' in content_type or 'application/octet-stream' in content_type:
                    with open(tiff_path, 'wb') as f:
                        for chunk in response.iter_content(chunk_size=8192):
                            f.write(chunk)
                    print("✅ GeoTIFF descargado. Procesando con Rasterio...")
                else:
                    print(f"⚠️ La respuesta no es TIFF. Tipo: {content_type}")
                    return None
            else:
                print(f"❌ Error API Topografía: {response.status_code} - {response.text[:200]}")
                return None
        except Exception as e:
            print(f"❌ Error de conexión: {e}")
            return None

        # Procesamiento GeoTIFF a CSV para Revit
        try:
            with rasterio.open(tiff_path) as dataset:
                band1 = dataset.read(1)
                rows, cols = np.where(band1 != dataset.nodata)
                xs = dataset.xy(rows, cols)[0]
                ys = dataset.xy(rows, cols)[1]
                zs = band1[rows, cols]

                MAX_POINTS = 10000
                if len(xs) > MAX_POINTS:
                    print(f"⚠️ Puntos exceden límite Revit ({len(xs)}). Submuestreando a {MAX_POINTS}...")
                    step = len(xs) // MAX_POINTS
                    xs = xs[::step]
                    ys = ys[::step]
                    zs = zs[::step]

                with open(csv_path, 'w') as f:
                    f.write("X,Y,Z\n")
                    for x, y, z in zip(xs, ys, zs):
                        f.write(f"{x:.3f},{y:.3f},{z:.3f}\n")
                
                print(f"✅ CSV topográfico generado: {csv_path} ({len(xs)} puntos)")
                return csv_path

        except Exception as e:
            print(f"❌ Error procesando GeoTIFF: {e}")
            return None

    def fetch_climate(self, latitude, longitude, output_dir="context_output"):
        print(f"☀️ Descargando datos climáticos para Lat:{latitude} Lon:{longitude}")
        
        os.makedirs(output_dir, exist_ok=True)
        climate_path = os.path.join(output_dir, "climate_data.json")

        url = f"https://archive-api.open-meteo.com/v1/archive?latitude={latitude}&longitude={longitude}&start_date=2023-01-01&end_date=2023-12-31&hourly=winddirection_100m,windspeed_100m,direct_normal_irradiance_instant&timezone=auto"

        try:
            response = requests.get(url)
            if response.status_code == 200:
                data = response.json()
                
                wind_dirs = [w for w in data['hourly']['winddirection_100m'] if w is not None]
                dominant_wind = max(set(wind_dirs), key=wind_dirs.count) if wind_dirs else 0
                
                solar_rad = [s for s in data['hourly']['direct_normal_irradiance_instant'] if s is not None]
                avg_solar = sum(solar_rad) / len(solar_rad) if solar_rad else 0

                climate_summary = {
                    "latitude": latitude,
                    "longitude": longitude,
                    "dominant_wind_direction_100m": dominant_wind,
                    "avg_direct_normal_irradiance_w_m2": round(avg_solar, 2)
                }

                with open(climate_path, 'w') as f:
                    json.dump(data, f)
                
                summary_path = os.path.join(output_dir, "climate_summary.json")
                with open(summary_path, 'w') as f:
                    json.dump(climate_summary, f, indent=2)

                print(f"✅ Datos climáticos descargados. Viento dominante: {dominant_wind}°. Radiación prom: {avg_solar:.0f} W/m2")
                return climate_summary

            else:
                print(f"❌ Error API Clima: {response.status_code}")
                return None
        except Exception as e:
            print(f"❌ Error de conexión clima: {e}")
            return None

if __name__ == "__main__":
    # LÍNEA 111: PEGUE AQUÍ SU API KEY DE OPENTOPOGRAPHY ENTRE LAS COMILLAS
    OPENTOPO_KEY = "9c89a797b18ede702687422b4974baa1"
    
    engine = ContextEngine(api_key_opentopo=OPENTOPO_KEY)
    
    LAT = -34.6037
    LON = -58.3816
    BOUNDING_BOX = {
        "south": LAT - 0.002, # Área un poco más grande para asegurar datos
        "north": LAT + 0.002,
        "west": LON - 0.002,
        "east": LON + 0.002
    }

    print("--- INICIANDO MOTOR DE CONTEXTO ---")
    
    engine.fetch_topography(south=BOUNDING_BOX['south'], north=BOUNDING_BOX['north'], 
                            west=BOUNDING_BOX['west'], east=BOUNDING_BOX['east'])
    
    engine.fetch_climate(latitude=LAT, longitude=LON)
    
    print("--- MOTOR DE CONTEXTO FINALIZADO ---")