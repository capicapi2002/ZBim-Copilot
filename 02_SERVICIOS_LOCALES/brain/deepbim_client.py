import socket
import json
import time
import select
from typing import Optional, Dict, Any, List

class DeepBIMClient:
    def __init__(self, scan_timeout: float = 1.0, command_timeout: float = 30.0):
        self.scan_timeout = scan_timeout
        self.command_timeout = command_timeout
        self.sock: Optional[socket.socket] = None
        self.port: Optional[int] = None
        self.request_id = 0

    def _send_json(self, payload: Dict[str, Any]) -> None:
        if not self.sock:
            raise RuntimeError("No hay conexión activa")
        message = json.dumps(payload) + "\n"
        self.sock.sendall(message.encode('utf-8'))

    def _recv_json(self, timeout: float) -> Optional[Dict[str, Any]]:
        if not self.sock:
            raise RuntimeError("No hay conexión activa")
        self.sock.settimeout(timeout)
        data = b''
        try:
            while True:
                chunk = self.sock.recv(4096)
                if not chunk:
                    break
                data += chunk
                if b'\n' in chunk:
                    break
            if not data:
                return None
            line = data.split(b'\n')[0].decode('utf-8').strip()
            return json.loads(line) if line else None
        except socket.timeout:
            return None
        except Exception:
            return None

    def scan_for_server(self) -> Optional[int]:
        print("Escaneando puertos 8080-8099...")
        for port in range(8080, 8100):
            try:
                test_sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                test_sock.settimeout(self.scan_timeout)
                result = test_sock.connect_ex(('127.0.0.1', port))
                test_sock.close()
                if result == 0:
                    print(f"Puerto {port} abierto. Intentando handshake MCP...")
                    if self._try_handshake_on_port(port):
                        print(f"✅ Handshake exitoso en puerto {port}")
                        return port
                    else:
                        print(f"Puerto {port} no responde al handshake MCP")
            except Exception:
                continue
        return None

    def _try_handshake_on_port(self, port: int) -> bool:
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.settimeout(self.command_timeout)
            sock.connect(('127.0.0.1', port))
            
            init_payload = {
                "jsonrpc": "2.0",
                "method": "initialize",
                "params": {
                    "protocolVersion": "0.1.0",
                    "capabilities": {},
                    "clientInfo": {"name": "DeepBIMClient", "version": "1.0"}
                },
                "id": 0
            }
            sock.sendall((json.dumps(init_payload) + "\n").encode('utf-8'))
            
            response = self._recv_from_socket(sock, timeout=self.command_timeout)
            if not response or "result" not in response:
                sock.close()
                return False
            
            initialized_payload = {
                "jsonrpc": "2.0",
                "method": "notifications/initialized",
                "params": {}
            }
            sock.sendall((json.dumps(initialized_payload) + "\n").encode('utf-8'))
            time.sleep(0.5)
            
            tools_payload = {
                "jsonrpc": "2.0",
                "method": "tools/list",
                "params": {},
                "id": 1
            }
            sock.sendall((json.dumps(tools_payload) + "\n").encode('utf-8'))
            tools_response = self._recv_from_socket(sock, timeout=self.command_timeout)
            sock.close()
            return tools_response is not None and "result" in tools_response
        except Exception:
            return False

    def _recv_from_socket(self, sock: socket.socket, timeout: float) -> Optional[Dict]:
        sock.settimeout(timeout)
        data = b''
        try:
            while True:
                chunk = sock.recv(4096)
                if not chunk:
                    break
                data += chunk
                if b'\n' in chunk:
                    break
            if not data:
                return None
            line = data.split(b'\n')[0].decode('utf-8').strip()
            return json.loads(line) if line else None
        except socket.timeout:
            return None
        except Exception:
            return None

    def connect(self, port: Optional[int] = None) -> bool:
        if port is None:
            port = self.scan_for_server()
            if port is None:
                print("No se encontró ninguna instancia de DeepBIM-MCP activa.")
                return False
        self.port = port
        
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.settimeout(self.command_timeout)
        try:
            self.sock.connect(('127.0.0.1', self.port))
        except Exception as e:
            print(f"Error al conectar al puerto {self.port}: {e}")
            return False
        
        init_payload = {
            "jsonrpc": "2.0",
            "method": "initialize",
            "params": {
                "protocolVersion": "0.1.0",
                "capabilities": {},
                "clientInfo": {"name": "DeepBIMClient", "version": "1.0"}
            },
            "id": self._next_id()
        }
        self._send_json(init_payload)
        init_response = self._recv_json(self.command_timeout)
        if not init_response or "result" not in init_response:
            print("Error en initialize handshake")
            self.sock.close()
            self.sock = None
            return False
        
        notif_payload = {
            "jsonrpc": "2.0",
            "method": "notifications/initialized",
            "params": {}
        }
        self._send_json(notif_payload)
        time.sleep(0.2)
        
        # Verificar conectividad con tools/list
        tools = self.list_tools()
        if tools is None:
            print("Handshake completado pero no se pudo obtener lista de herramientas")
        return True

    def _next_id(self) -> int:
        self.request_id += 1
        return self.request_id

    def list_tools(self) -> Optional[List[Dict]]:
        if not self.sock:
            raise RuntimeError("No conectado")
        payload = {
            "jsonrpc": "2.0",
            "method": "tools/list",
            "params": {},
            "id": self._next_id()
        }
        self._send_json(payload)
        response = self._recv_json(self.command_timeout)
        if response and "result" in response:
            return response["result"].get("tools", [])
        return None

    def call_tool(self, tool_name: str, arguments: Dict[str, Any]) -> Optional[Dict]:
        if not self.sock:
            raise RuntimeError("No conectado")
        payload = {
            "jsonrpc": "2.0",
            "method": "tools/call",
            "params": {
                "name": tool_name,
                "arguments": arguments
            },
            "id": self._next_id()
        }
        self._send_json(payload)
        return self._recv_json(self.command_timeout)

    def close(self):
        if self.sock:
            self.sock.close()
            self.sock = None

if __name__ == "__main__":
    client = DeepBIMClient(scan_timeout=1.0, command_timeout=30.0)
    if client.connect():
        print(f"Conectado a DeepBIM-MCP en puerto {client.port}")
        tools = client.list_tools()
        if tools:
            print(f"Herramientas disponibles ({len(tools)}):")
            for t in tools[:5]:
                print(f"  - {t.get('name')}: {t.get('description', '')[:60]}")
        client.close()
    else:
        print("No se pudo conectar a ninguna instancia de Revit con DeepBIM-MCP")