from fastapi import FastAPI, Request
import json
import os

app = FastAPI()

@app.post("/oas/")
async def receive_oas(request: Request):
    try:
        # 1. Recibir el JSON enviado desde el panel de Revit
        body = await request.json()
        project_name = body.get("ProjectName", "Desconocido")
        print(f"✅ JSON recibido correctamente. Proyecto: {project_name}")
        
        # 2. Guardar el JSON en un archivo local para que el AddIn de Revit pueda leerlo
        file_path = os.path.join(os.path.dirname(__file__), "pending_oas.json")
        with open(file_path, "w", encoding="utf-8") as f:
            json.dump(body, f, indent=2, ensure_ascii=False)
            
        print(f"💾 Datos guardados exitosamente en: {file_path}")
        
        # 3. Responder al panel de Revit
        return {"status": "success", "message": "Datos recibidos y listos para ejecución"}
        
    except Exception as e:
        print(f"❌ Error al procesar JSON: {e}")
        return {"status": "error", "message": str(e)}

if __name__ == "__main__":
    import uvicorn
    print("🚀 Iniciando servidor ZBIM-Copilot en http://localhost:8080")
    uvicorn.run(app, host="0.0.0.0", port=8080)