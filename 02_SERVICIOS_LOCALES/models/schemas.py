from pydantic import BaseModel, Field
from typing import Optional, Dict, Any, List
from enum import Enum

class TaskAction(str, Enum):
    READ_MODEL = "read_model"           # Usar MCP Nativo Revit 2027
    NATIVE_ACTION = "native_action"     # Usar funciones nativas (Numbering, etc.)
    WRITE_CODE = "write_code"           # Usar DeepBIM-MCP + Text2MBL/Kimi

class ProcessingLevel(str, Enum):
    LEVEL_0 = "local_zero_tokens"       # Hypar, ChromaDB, Gemma, Lecturas
    LEVEL_1 = "free_tier"               # OpenRouter
    LEVEL_2 = "premium_kimi"            # Kimi K2.5 (Cacheado)

class BIMTask(BaseModel):
    task_id: str
    type: str
    description: str
    context: Dict = {}

class BIMCommand(BaseModel):
    task_id: str
    method: str = "ejecutar_accion_kimi"
    code: Optional[str] = None
    params: Optional[Dict[str, Any]] = None
    target_port: int = 8082

class MCPResponse(BaseModel):
    success: bool
    message: str
    port: int | str