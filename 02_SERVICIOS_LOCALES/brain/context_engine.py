import requests
import rasterio
import numpy as np
import os
import json

class ContextEngine:
    def __init__(self, api_key_opentopo):
        self.api_key = api_key_opentopo

    def fetch_topography(self, lat, lon, radius_m=200, output_dir="context_output"):
        """
        Descarga topografía desde OpenTopography calculando bounding box dinámicamente.
        :param lat: Latitud central
        :param lon: Longitud central
        :param radius_m: Radio en metros (default 200m)
        :param output_dir: Directorio de salida
        """
        # Calcular bounding box dinámicamente
        delta = radius_m / 111000.0  # Aproximación grados por metro
        south = lat - delta
        north = lat + delta
        west = lon - delta
        east = lon + delta
        
        print(f"🗺️ Descargando topografía: S:{south} N:{north} W:{west} E:{east} (radio: {radius_m}m)")
        
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
        """
        Descarga datos climáticos ACTUALES (últimas 24 horas) desde Open-Meteo Forecast API
        y devuelve un resumen con las claves exactas que espera el panel Odysseus:
        temperature_avg, humidity_avg, wind_speed_avg,
        wind_direction_dominant, solar_radiation_avg
        
        CAMBIO CRÍTICO: Usa forecast API en lugar de archive API para obtener datos actuales.
        """
        print(f"☀️ Descargando datos climáticos ACTUALES para Lat:{latitude} Lon:{longitude}")
        
        os.makedirs(output_dir, exist_ok=True)
        climate_path = os.path.join(output_dir, "climate_data.json")

        # CAMBIO CRÍTICO: Usar forecast API (datos actuales) en lugar de archive API (datos históricos 2023)
        # Nombres de variables corregidos para forecast API (con guiones bajos)
        variables = "temperature_2m,relative_humidity_2m,wind_speed_10m,wind_direction_10m,direct_radiation"
        url = (f"https://api.open-meteo.com/v1/forecast?"
               f"latitude={latitude}&longitude={longitude}"
               f"&hourly={variables}"
               f"&timezone=auto"
               f"&forecast_days=1")  # Solo hoy (últimas 24 horas)

        try:
            response = requests.get(url, timeout=10)
            if response.status_code == 200:
                data = response.json()
                
                # Guardar datos crudos
                with open(climate_path, 'w') as f:
                    json.dump(data, f)

                # Extraer listas limpias (nombres corregidos para forecast API)
                temps   = [v for v in data['hourly']['temperature_2m'] if v is not None]
                humids  = [v for v in data['hourly']['relative_humidity_2m'] if v is not None]
                wspeeds = [v for v in data['hourly']['wind_speed_10m'] if v is not None]
                wdirs   = [v for v in data['hourly']['wind_direction_10m'] if v is not None]
                solars  = [v for v in data['hourly']['direct_radiation'] if v is not None]

                avg_temp   = sum(temps) / len(temps) if temps else 0
                avg_humid  = sum(humids) / len(humids) if humids else 0
                avg_wspeed = sum(wspeeds) / len(wspeeds) if wspeeds else 0
                avg_solar  = sum(solars) / len(solars) if solars else 0
                dominant_wind = max(set(wdirs), key=wdirs.count) if wdirs else 0

                # Claves exactas que espera el frontend (showClimateModal)
                climate_summary = {
                    "temperature_avg": round(avg_temp, 1),
                    "humidity_avg": round(avg_humid, 1),
                    "wind_speed_avg": round(avg_wspeed, 1),
                    "wind_direction_dominant": str(dominant_wind),
                    "solar_radiation_avg": round(avg_solar, 2)
                }

                # Guardar resumen para uso futuro
                summary_path = os.path.join(output_dir, "climate_summary.json")
                with open(summary_path, 'w') as f:
                    json.dump(climate_summary, f, indent=2)

                print(f"✅ Datos climáticos ACTUALES descargados. Temp: {avg_temp:.1f}°C, "
                      f"Humedad: {avg_humid:.1f}%, Viento: {avg_wspeed:.1f} km/h")
                return climate_summary

            else:
                print(f"❌ Error API Clima: {response.status_code}")
                return None
        except Exception as e:
            print(f"❌ Error de conexión clima: {e}")
            return None

    # ============================================================
    # FASE E: FUNCIÓN DE CONTEXTO INTEGRADO
    # ============================================================

    def geocode_location(self, location_text):
        """
        [FASE E] Geocodifica una ubicación (texto libre) a coordenadas lat/lon usando Nominatim.
        """
        try:
            url = "https://nominatim.openstreetmap.org/search"
            params = {
                "q": location_text,
                "format": "json",
                "limit": 1
            }
            headers = {
                "User-Agent": "ZBIM-Copilot/1.0"
            }
            response = requests.get(url, params=params, headers=headers, timeout=10)
            if response.status_code == 200:
                results = response.json()
                if results and len(results) > 0:
                    lat = float(results[0]["lat"])
                    lon = float(results[0]["lon"])
                    print(f"📍 Geocodificación exitosa: {location_text} → Lat:{lat}, Lon:{lon}")
                    return lat, lon
            print(f"⚠️ No se pudo geocodificar: {location_text}")
            return None, None
        except Exception as e:
            print(f"❌ Error en geocodificación: {e}")
            return None, None

    def enrich_context(self, location=None, latitude=None, longitude=None, radius_m=200):
        """
        [FASE E] Obtiene datos de topografía y clima para una ubicación dada.
        Acepta texto de ubicación o coordenadas directas.
        Devuelve un diccionario con la estructura que espera la UI.
        
        :param location: Texto de ubicación (se geocodifica)
        :param latitude: Latitud (si se proporcionan coordenadas directas)
        :param longitude: Longitud (si se proporcionan coordenadas directas)
        :param radius_m: Radio en metros para el bounding box de topografía (default 200m)
        """
        print(f"🌍 Obteniendo contexto con radio: {radius_m}m")
        
        # Paso 1: Obtener coordenadas
        lat, lon = None, None
        if latitude is not None and longitude is not None:
            lat, lon = latitude, longitude
            print(f"📍 Usando coordenadas directas: Lat:{lat}, Lon:{lon}")
        elif location:
            lat, lon = self.geocode_location(location)
            if lat is None or lon is None:
                return {"topography": None, "climate": None}
        else:
            print("❌ Se requiere location o latitude/longitude")
            return {"topography": None, "climate": None}

        # Paso 2: Topografía
        topography_data = None
        try:
            csv_path = self.fetch_topography(lat, lon, radius_m=radius_m)
            if csv_path and os.path.exists(csv_path):
                # Leer CSV y convertir a formato JSON
                points = []
                min_x, max_x = float('inf'), float('-inf')
                min_y, max_y = float('inf'), float('-inf')
                
                with open(csv_path, 'r') as f:
                    lines = f.readlines()[1:]  # Saltar header
                    for line in lines:
                        parts = line.strip().split(',')
                        if len(parts) == 3:
                            x, y, z = float(parts[0]), float(parts[1]), float(parts[2])
                            points.append({"x": x, "y": y, "z": z})
                            min_x = min(min_x, x)
                            max_x = max(max_x, x)
                            min_y = min(min_y, y)
                            max_y = max(max_y, y)
                
                topography_data = {
                    "points": points,
                    "bounds": {
                        "min_x": min_x,
                        "max_x": max_x,
                        "min_y": min_y,
                        "max_y": max_y
                    }
                }
                print(f"✅ Topografía procesada: {len(points)} puntos")
        except Exception as e:
            print(f"❌ Error obteniendo topografía: {e}")

        # Paso 3: Clima
        climate_data = None
        try:
            climate_summary = self.fetch_climate(lat, lon)
            if climate_summary:
                climate_data = climate_summary   # Ya tiene las claves correctas
                print(f"✅ Clima procesado: Temp {climate_data.get('temperature_avg')}°C")
                
                # [NUEVO] Guardar JSON de clima en climate.json
                output_dir = "context_output"
                os.makedirs(output_dir, exist_ok=True)
                climate_json_path = os.path.join(output_dir, "climate.json")
                with open(climate_json_path, 'w', encoding='utf-8') as f:
                    json.dump(climate_data, f, indent=2, ensure_ascii=False)
                print(f"💾 Clima guardado en: {climate_json_path}")
        except Exception as e:
            print(f"❌ Error obteniendo clima: {e}")

        return {
            "topography": topography_data,
            "climate": climate_data
        }

    def get_context(self, location_text):
        """
        [FASE E] Wrapper para compatibilidad - llama a enrich_context con ubicación de texto.
        """
        return self.enrich_context(location=location_text)


if __name__ == "__main__":
    # LÍNEA 111: PEGUE AQUÍ SU API KEY DE OPENTOPOGRAPHY ENTRE LAS COMILLAS
    OPENTOPO_KEY = "9c89a797b18ede702687422b4974baa1"
    
    engine = ContextEngine(api_key_opentopo=OPENTOPO_KEY)
    
    LAT = -34.6037
    LON = -58.3816
    RADIUS = 300  # Radio en metros

    print("--- INICIANDO MOTOR DE CONTEXTO ---")
    
    # Nueva firma: lat, lon, radius_m
    engine.fetch_topography(LAT, LON, radius_m=RADIUS)
    
    engine.fetch_climate(latitude=LAT, longitude=LON)
    
    # Prueba de enrich_context con coordenadas y radio
    print("\n--- PRUEBA enrich_context CON COORDENADAS ---")
    engine.enrich_context(latitude=LAT, longitude=LON, radius_m=RADIUS)
    
    print("--- MOTOR DE CONTEXTO FINALIZADO ---")