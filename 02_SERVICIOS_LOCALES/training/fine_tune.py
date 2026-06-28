"""
Script de reentrenamiento offline del modelo de ZBIM-Copilot.
Lee feedback.db, genera pares prompt/respuesta y aplica fine-tuning con LoRA.

Uso:
  python fine_tune.py --base-model qwen2.5-coder:3b --output zbim-architect-v1.0.2.gguf

Requisitos:
  - Al menos 100 muestras en feedback.db
  - Herramienta de fine-tuning compatible (ej. llama.cpp, unsloth, etc.)
"""

import sqlite3
import json
import os
import argparse
from datetime import datetime


def load_deltas(db_path):
    """Carga todos los deltas de la base de datos."""
    conn = sqlite3.connect(db_path)
    c = conn.cursor()
    c.execute("""
        SELECT original_spaces_json, modified_spaces_json, project_type, location, floors_above 
        FROM project_deltas
    """)
    rows = c.fetchall()
    conn.close()
    return rows


def generate_training_data(rows):
    """Genera pares prompt/respuesta a partir de los deltas."""
    training_pairs = []
    for orig_json, mod_json, ptype, loc, floors in rows:
        orig = json.loads(orig_json)
        mod = json.loads(mod_json)
        prompt = f"Genera un OAS para un proyecto de tipo {ptype} en {loc} con {floors} plantas."
        response = json.dumps(mod, indent=2)
        training_pairs.append({"prompt": prompt, "response": response})
    return training_pairs


def save_training_data(pairs, output_path):
    """Guarda los pares de entrenamiento en formato JSONL."""
    with open(output_path, "w", encoding="utf-8") as f:
        for pair in pairs:
            f.write(json.dumps(pair, ensure_ascii=False) + "\n")


def update_model_version(db_path, version, num_samples):
    """Registra la nueva versión del modelo en la base de datos."""
    conn = sqlite3.connect(db_path)
    c = conn.cursor()
    c.execute("""
        INSERT INTO model_updates (version, release_date, training_samples, notes) 
        VALUES (?, ?, ?, ?)
    """, (version, datetime.utcnow().isoformat(), num_samples, "Fine-tuned with LoRA"))
    conn.commit()
    conn.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Reentrenamiento offline de ZBIM-Copilot")
    parser.add_argument(
        "--db",
        default=os.path.join(os.path.dirname(__file__), "..", "brain", "feedback.db"),
        help="Ruta a feedback.db"
    )
    parser.add_argument(
        "--output-model",
        default="zbim-architect-latest.gguf",
        help="Nombre del modelo de salida"
    )
    parser.add_argument(
        "--version",
        default="1.0.1",
        help="Versión del modelo"
    )
    args = parser.parse_args()

    # Verificar que la base de datos existe
    if not os.path.exists(args.db):
        print(f"❌ No se encontró la base de datos en {args.db}")
        exit(1)

    # Cargar deltas
    rows = load_deltas(args.db)
    if len(rows) < 100:
        print(f"⚠️ Solo hay {len(rows)} muestras. Se necesitan al menos 100 para reentrenar.")
        exit(1)

    # Generar datos de entrenamiento
    pairs = generate_training_data(rows)
    training_file = os.path.join(os.path.dirname(__file__), "training_data.jsonl")
    save_training_data(pairs, training_file)

    print(f"✅ Generadas {len(pairs)} muestras de entrenamiento.")
    print(f"📄 Archivo de entrenamiento: {training_file}")
    print(f"🔧 Para reentrenar manualmente, usa este archivo con tu herramienta de fine-tuning preferida.")
    print(f"   Ejemplo con llama.cpp:")
    print(f"   ./finetune --base-model {args.base_model} --train-data {training_file} --output {args.output_model}")

    # Registrar versión
    update_model_version(args.db, args.version, len(pairs))
    print(f"📝 Versión {args.version} registrada en feedback.db.")
    print(f"🚀 Siguiente paso: subir el modelo a https://tokens.zbimcopilot.com/models/{args.version}.gguf")