from models.schemas import BIMTask, ProcessingLevel, TaskAction

class TaskRouter:
    def __init__(self):
        pass

    def route(self, task: BIMTask) -> dict:
        # 1. ¿Es una consulta de datos? -> Revit 2027 MCP Server (Nivel 0)
        if task.type == "query":
            return {"level": ProcessingLevel.LEVEL_0, "action": TaskAction.READ_MODEL, "engine": "revit_2027_mcp"}
            
        # 2. ¿Es una tarea que Revit 2027 ya hace solo? -> API Nativa (Nivel 0)
        if task.type in ["numbering", "hosted_wall"]:
            return {"level": ProcessingLevel.LEVEL_0, "action": TaskAction.NATIVE_ACTION, "engine": "revit_native_api"}

        # 3. ¿Es diseño complejo? -> DeepBIM-MCP + Kimi K2.5 (Nivel 2)
        if task.type in ["architectural", "mep"]:
            return {"level": ProcessingLevel.LEVEL_2, "action": TaskAction.WRITE_CODE, "engine": "deepbim_mcp"}

        # 4. Default
        return {"level": ProcessingLevel.LEVEL_1, "action": TaskAction.WRITE_CODE, "engine": "deepbim_mcp"}