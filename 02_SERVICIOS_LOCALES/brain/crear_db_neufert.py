import json
import sqlite3
import glob
import os

# Configuración
DB_PATH = "neufert_data.db"
JSON_PATTERN = "neufert_parte_*.json"

def crear_base_de_datos():
    print("🏗️ Iniciando creación de la Base de Datos Neufert (Esquema 2.0)...")
    
    # 1. Conectar a SQLite
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()

    # 2. Crear la tabla con el esquema completo del Prompt 2.0
    # Nota: Los campos complejos (tabular_data, spatial_relationships, tags) 
    # se guardan como texto JSON, que es el estándar profesional en SQLite.
    cursor.execute('''
        CREATE TABLE IF NOT EXISTS neufert_knowledge (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            data_type TEXT,
            domain TEXT,
            element TEXT,
            parameter_or_subject TEXT,
            value_type TEXT,
            min_value REAL,
            max_value REAL,
            typical_value REAL,
            unit TEXT,
            rule_description TEXT,
            formula_expression TEXT,
            formula_variables TEXT,
            tabular_data TEXT,
            spatial_relationships TEXT,
            condition TEXT,
            normative_reference TEXT,
            importance_level TEXT,
            source_reference TEXT,
            tags TEXT
        )
    ''')

    # 3. Buscar todos los archivos JSON
    json_files = sorted(glob.glob(JSON_PATTERN))
    if not json_files:
        print(f"⚠️ ERROR: No se encontraron archivos que coincidan con '{JSON_PATTERN}' en esta carpeta.")
        print("Asegúrese de que los archivos .json estén en la misma carpeta que este script.")
        return

    total_registros = 0
    for file in json_files:
        print(f"📖 Procesando {os.path.basename(file)}...")
        try:
            with open(file, 'r', encoding='utf-8') as f:
                data = json.load(f)
                
                # Insertar cada regla en la base de datos
                for item in data:
                    cursor.execute('''
                        INSERT INTO neufert_knowledge 
                        (data_type, domain, element, parameter_or_subject, value_type, 
                         min_value, max_value, typical_value, unit, rule_description, 
                         formula_expression, formula_variables, tabular_data, spatial_relationships, 
                         condition, normative_reference, importance_level, source_reference, tags)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    ''', (
                        item.get('data_type'),
                        item.get('domain'),
                        item.get('element'),
                        item.get('parameter_or_subject'),
                        item.get('value_type'),
                        item.get('min_value'),
                        item.get('max_value'),
                        item.get('typical_value'),
                        item.get('unit'),
                        item.get('rule_description'),
                        item.get('formula_expression'),
                        item.get('formula_variables'),
                        json.dumps(item.get('tabular_data')) if item.get('tabular_data') else None,
                        json.dumps(item.get('spatial_relationships')) if item.get('spatial_relationships') else None,
                        item.get('condition'),
                        item.get('normative_reference'),
                        item.get('importance_level'),
                        item.get('source_reference'),
                        json.dumps(item.get('tags')) if item.get('tags') else None
                    ))
                    total_registros += 1
        except json.JSONDecodeError:
            print(f"❌ ERROR: El archivo {file} no es un JSON válido. Revise la salida de DeepSeek.")
        except Exception as e:
            print(f"❌ ERROR al procesar {file}: {e}")

    # 4. Guardar cambios y cerrar
    conn.commit()
    conn.close()
    
    print(f"\n✅ ¡ÉXITO! Se insertaron {total_registros} registros arquitectónicos en {DB_PATH}")
    
    # 5. Prueba rápida de consulta para verificar que funciona
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    
    print("\n🔍 PRUEBA DE CONSULTA (Ejemplo: Reglas de Accesibilidad o Escaleras):")
    cursor.execute("""
        SELECT element, parameter_or_subject, typical_value, unit, normative_reference 
        FROM neufert_knowledge 
        WHERE domain IN ('Accesibilidad', 'Ergonomia') 
        LIMIT 5
    """)
    
    resultados = cursor.fetchall()
    if resultados:
        for row in resultados:
            print(f"   • {row[0]} | {row[1]}: {row[2]} {row[3]} (Norma: {row[4]})")
    else:
        print("   (No se encontraron resultados de prueba, pero la base de datos se creó)")
        
    conn.close()
    print("\n🎉 Proceso completado. La base de datos está lista para ser usada por el Agente IA.")

if __name__ == "__main__":
    crear_base_de_datos()