import requests
import rasterio
from rasterio.enums import Resampling
import numpy as np
import os
import json
import math
import xml.etree.ElementTree as ET

try:
    from shapely.geometry import Polygon, Point
    SHAPELY_AVAILABLE = True
except ImportError:
    SHAPELY_AVAILABLE = False
    print("⚠️ Shapely no está instalado. No se podrá recortar topografía con polígonos KML.")

class ContextEngine:
    def __init__(self, api_key_opentopo, google_api_key=None):
        self.api_key = api_key_opentopo
        self.google_api_key = google_api_key

    def parse_kml_polygon(self, kml_string):
        """Parsea KML y devuelve (lista_de_tuplas, Polygon o None)."""
        try:
            if not kml_string or kml_string.strip() == "":
                return None, None
            root = ET.fromstring(kml_string.strip())
            ns = {"kml": "http://www.opengis.net/kml/2.2"}
            coordinates_nodes = root.findall(".//kml:coordinates", ns)
            if not coordinates_nodes:
                coordinates_nodes = root.findall(".//coordinates")
            coords_list = []
            for node in coordinates_nodes:
                coords_text = node.text.strip()
                for coord_line in coords_text.split():
                    parts = coord_line.strip().split(",")
                    if len(parts) >= 2:
                        lon, lat = float(parts[0]), float(parts[1])
                        coords_list.append((lon, lat))
                if coords_list:
                    break
            if not coords_list:
                return None, None
            if coords_list[0] != coords_list[-1]:
                coords_list.append(coords_list[0])
            if SHAPELY_AVAILABLE:
                polygon = Polygon(coords_list)
                return coords_list, polygon
            return coords_list, None
        except Exception as e:
            print(f"❌ Error parseando KML: {e}")
            return None, None

    def fetch_topography(self, lat, lon, radius_m=200, output_dir="context_output",
                         clip_polygon=None, rotation_angle_deg=0):
        """
        Descarga DEM de OpenTopography y genera puntos con resolución adaptativa.
        Si se proporciona un ángulo, los puntos se rotan alrededor del centro
        de la parcela antes de obtener las elevaciones.
        """
        MIN_DOWNLOAD_RADIUS = 200
        download_radius = max(radius_m, MIN_DOWNLOAD_RADIUS)

        # Resolución ultra‑densa
        if radius_m <= 45:
            target_spacing_m = 0.1          # 10 cm entre puntos
        elif radius_m < 100:
            target_spacing_m = 0.25         # 25 cm
        elif radius_m < 200:
            target_spacing_m = 2.0
        else:
            target_spacing_m = 5.0

        delta = download_radius / 111000.0
        south = lat - delta
        north = lat + delta
        west = lon - delta
        east = lon + delta

        print(f"🗺️ Descargando topografía: S:{south:.5f} N:{north:.5f} W:{west:.5f} E:{east:.5f} "
              f"(solicitado: {radius_m}m, resolución: {target_spacing_m}m)")

        os.makedirs(output_dir, exist_ok=True)
        tiff_path = os.path.join(output_dir, "terrain.tif")
        csv_path = os.path.join(output_dir, "terrain_revit.csv")

        base_url = "https://portal.opentopography.org/API/globaldem"
        params = {
            "demtype": "SRTMGL1",
            "south": south, "north": north,
            "west": west, "east": east,
            "outputFormat": "GTiff",
            "API_Key": self.api_key
        }

        try:
            response = requests.get(base_url, params=params, stream=True, timeout=20)
            if response.status_code == 200:
                content_type = response.headers.get('content-type', '')
                if 'image/tiff' in content_type or 'application/octet-stream' in content_type:
                    with open(tiff_path, 'wb') as f:
                        for chunk in response.iter_content(chunk_size=8192):
                            f.write(chunk)
                else:
                    print(f"⚠️ Respuesta no TIFF: {content_type}")
                    return None
            else:
                print(f"❌ Error API OpenTopography: {response.status_code}")
                return None
        except Exception as e:
            print(f"❌ Excepción durante descarga: {e}")
            return None

        try:
            with rasterio.open(tiff_path) as dataset:
                # Definir la zona de muestreo
                if clip_polygon:
                    minx, miny, maxx, maxy = clip_polygon.bounds
                else:
                    lat_deg_per_m = 1.0 / 111000.0
                    lon_deg_per_m = 1.0 / (111000.0 * math.cos(math.radians(lat)))
                    rad_deg_lat = radius_m * lat_deg_per_m
                    rad_deg_lon = radius_m * lon_deg_per_m
                    minx = lon - rad_deg_lon
                    maxx = lon + rad_deg_lon
                    miny = lat - rad_deg_lat
                    maxy = lat + rad_deg_lat

                lat_step_deg = target_spacing_m / 111000.0
                lon_step_deg = target_spacing_m / (111000.0 * math.cos(math.radians(lat)))

                xs = np.arange(minx, maxx + lon_step_deg, lon_step_deg)
                ys = np.arange(miny, maxy + lat_step_deg, lat_step_deg)
                xx, yy = np.meshgrid(xs, ys)
                points_lon = xx.ravel()
                points_lat = yy.ravel()

                # Filtrar dentro del polígono o del círculo
                if clip_polygon:
                    inside = np.array([clip_polygon.contains(Point(lo, la))
                                       for lo, la in zip(points_lon, points_lat)])
                else:
                    center_lon, center_lat = lon, lat
                    lon_dist = (points_lon - center_lon) * math.cos(math.radians(center_lat))
                    lat_dist = points_lat - center_lat
                    dist = np.sqrt(lon_dist**2 + lat_dist**2)
                    max_dist_deg = radius_m / 111000.0
                    inside = dist <= max_dist_deg

                points_lon = points_lon[inside]
                points_lat = points_lat[inside]

                if len(points_lon) == 0:
                    points_lon = np.array([lon])
                    points_lat = np.array([lat])

                # --- ROTACIÓN alrededor del centro de la parcela ---
                if rotation_angle_deg != 0:
                    center_lon_rot = np.mean(points_lon)
                    center_lat_rot = np.mean(points_lat)
                    theta = math.radians(rotation_angle_deg)
                    cos_a, sin_a = math.cos(theta), math.sin(theta)
                    dx = points_lon - center_lon_rot
                    dy = points_lat - center_lat_rot
                    points_lon = center_lon_rot + dx * cos_a - dy * sin_a
                    points_lat = center_lat_rot + dx * sin_a + dy * cos_a

                # Muestrear elevaciones en cada punto
                xy = list(zip(points_lon, points_lat))
                elevs = [val[0] for val in dataset.sample(xy)]
                zs = np.array(elevs)

                nodata = dataset.nodata
                if nodata is not None:
                    valid = zs != nodata
                    points_lon = points_lon[valid]
                    points_lat = points_lat[valid]
                    zs = zs[valid]

                MAX_POINTS = 50000   # Más puntos = superficie más suave
                if len(points_lon) > MAX_POINTS:
                    idx = np.linspace(0, len(points_lon)-1, MAX_POINTS, dtype=int)
                    points_lon = points_lon[idx]
                    points_lat = points_lat[idx]
                    zs = zs[idx]

                with open(csv_path, 'w') as f:
                    f.write("X,Y,Z\n")
                    for lo, la, z in zip(points_lon, points_lat, zs):
                        f.write(f"{lo:.6f},{la:.6f},{z:.3f}\n")

                print(f"✅ Puntos topográficos generados: {len(points_lon)}")
                return csv_path
        except Exception as e:
            print(f"❌ Error procesando dataset: {e}")
            return None

    def fetch_climate(self, latitude, longitude, output_dir="context_output"):
        os.makedirs(output_dir, exist_ok=True)
        variables = "temperature_2m,relative_humidity_2m,wind_speed_10m,wind_direction_10m,direct_radiation"
        url = (f"https://api.open-meteo.com/v1/forecast?"
               f"latitude={latitude}&longitude={longitude}"
               f"&hourly={variables}&timezone=auto&forecast_days=1")
        try:
            response = requests.get(url, timeout=10)
            if response.status_code == 200:
                data = response.json()
                temps = [v for v in data['hourly']['temperature_2m'] if v is not None]
                humids = [v for v in data['hourly']['relative_humidity_2m'] if v is not None]
                wspeeds = [v for v in data['hourly']['wind_speed_10m'] if v is not None]
                wdirs = [v for v in data['hourly']['wind_direction_10m'] if v is not None]
                solars = [v for v in data['hourly']['direct_radiation'] if v is not None]
                return {
                    "temperature_avg": round(sum(temps)/len(temps), 1) if temps else 15.0,
                    "humidity_avg": round(sum(humids)/len(humids), 1) if humids else 60.0,
                    "wind_speed_avg": round(sum(wspeeds)/len(wspeeds), 1) if wspeeds else 10.0,
                    "wind_direction_dominant": str(max(set(wdirs), key=wdirs.count) if wdirs else "N"),
                    "solar_radiation_avg": round(sum(solars)/len(solars), 2) if solars else 150.0
                }
        except Exception as e:
            print(f"⚠️ Error extrayendo datos climáticos: {e}")
        return {
            "temperature_avg": 15.0, "humidity_avg": 60.0,
            "wind_speed_avg": 10.0, "wind_direction_dominant": "N",
            "solar_radiation_avg": 150.0
        }

    def geocode_location(self, location_text):
        try:
            url = "https://nominatim.openstreetmap.org/search"
            params = {"q": location_text, "format": "json", "limit": 1}
            headers = {"User-Agent": "ZBIM-Copilot/1.0"}
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

    def enrich_context(self, location=None, latitude=None, longitude=None,
                       radius_m=200, kml_string=None, rotation_angle=0):
        """Orquesta topografía, clima y contorno. rotation_angle en grados."""
        topography_data = {"points": [], "bounds": {"min_x": 0.0, "max_x": 0.0, "min_y": 0.0, "max_y": 0.0}}
        contour_data = {"coordinates": []}
        climate_data = {
            "temperature_avg": 15.0, "humidity_avg": 60.0,
            "wind_speed_avg": 10.0, "wind_direction_dominant": "N", "solar_radiation_avg": 150.0
        }
        lat, lon = latitude, longitude
        if lat is None or lon is None:
            return {"topography": topography_data, "climate": climate_data, "contour": contour_data}

        contour_coords = None
        clip_polygon = None
        if kml_string:
            contour_coords, clip_polygon = self.parse_kml_polygon(kml_string)
            if contour_coords:
                print(f"✅ Polígono KML extraído con {len(contour_coords)} vértices")
            else:
                print("⚠️ No se pudo extraer un polígono del KML. Se usará el radio.")

        try:
            csv_path = self.fetch_topography(lat, lon, radius_m=radius_m,
                                             clip_polygon=clip_polygon,
                                             rotation_angle_deg=rotation_angle)
            if csv_path and os.path.exists(csv_path):
                points = []
                with open(csv_path, 'r') as f:
                    lines = f.readlines()[1:]
                    for line in lines:
                        parts = line.strip().split(',')
                        if len(parts) == 3:
                            points.append({
                                "lat": float(parts[1]),
                                "lon": float(parts[0]),
                                "elevation": float(parts[2])
                            })
                if points:
                    topography_data = {
                        "points": points,
                        "bounds": {
                            "min_x": min(p["lon"] for p in points),
                            "max_x": max(p["lon"] for p in points),
                            "min_y": min(p["lat"] for p in points),
                            "max_y": max(p["lat"] for p in points)
                        }
                    }
        except Exception as e:
            print(f"❌ Error obteniendo topografía: {e}")

        try:
            climate_summary = self.fetch_climate(lat, lon)
            if climate_summary:
                climate_data = climate_summary
        except Exception as e:
            print(f"❌ Error obteniendo clima: {e}")

        if contour_coords:
            contour_data = {
                "coordinates": [{"lat": lat, "lon": lon} for lon, lat in contour_coords]
            }

        return {
            "topography": topography_data,
            "climate": climate_data,
            "contour": contour_data
        }

    def get_context(self, location_text):
        return self.enrich_context(location=location_text)


if __name__ == "__main__":
    OPENTOPO_KEY = "9c89a797b18ede702687422b4974baa1"
    engine = ContextEngine(api_key_opentopo=OPENTOPO_KEY)
    LAT, LON, RADIUS = -34.6037, -58.3816, 300
    print("--- INICIANDO MOTOR DE CONTEXTO ---")
    engine.fetch_topography(LAT, LON, radius_m=RADIUS)
    engine.fetch_climate(latitude=LAT, longitude=LON)
    print("\n--- PRUEBA enrich_context ---")
    engine.enrich_context(latitude=LAT, longitude=LON, radius_m=RADIUS)
    print("--- FINALIZADO ---")