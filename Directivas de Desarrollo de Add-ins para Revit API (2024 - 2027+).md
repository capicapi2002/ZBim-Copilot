Directivas de Desarrollo de Add-ins para Revit API (2024 - 2027+)

Usa este prompt de sistema para instruir a cualquier modelo de IA sobre cómo escribir código C# moderno, eficiente y robusto para Autodesk Revit, previniendo errores comunes de arquitectura, hilos y deprecación de APIs.

1. Reglas de Versión de API y Obsolescencia (Crucial para Revit 2024+)

Prohibición de TopographySurface: No utilices TopographySurface.Create() ni métodos asociados de la clase TopographySurface. Esta clase está obsoleta desde Revit 2024 y su comportamiento en Revit 2026/2027 es inestable o nulo.

Uso Obligatorio de Toposolid: Para generar terrenos o topografías, debes implementar obligatoriamente la clase Toposolid.

La instanciación base requiere un perfil cerrado (CurveLoop), un tipo (ToposolidType) y un nivel (Level).

Para dar relieve irregular a partir de nubes de puntos, activa el editor de forma del objeto usando toposolid.GetSlabShapeEditor() y añade los puntos interiores iterativamente con SlabShapeEditor.DrawPoint(XYZ).

2. Robustez Geométrica y Prevención de Excepciones

Filtro de Puntos Duplicados (XY Coincidentes): Antes de pasar cualquier colección de puntos a la API de Revit para generar superficies o elementos basados en mallas, debes sanitizar y limpiar los datos usando LINQ. Dos puntos con idéntica ubicación en planta (mismo XY pero diferente Z) provocarán un fallo de triangulación y lanzarán un ArgumentException.

// Ejemplo de sanitización con tolerancia milimétrica (redondeo a 2 o 3 decimales en pies)
var puntosLimpios = puntosOriginales
    .GroupBy(p => new { X = Math.Round(p.X, 2), Y = Math.Round(p.Y, 2) })
    .Select(g => g.First())
    .ToList();


Regla de las 20 Millas y Coordenadas Grandes: Revit pierde precisión flotante de GPU y falla geométricamente si se dibujan elementos a más de 30 km (20 millas) del Origen Interno del Proyecto.

Si los puntos de entrada son coordenadas geográficas (Latitud/Longitud) o coordenadas proyectadas reales de gran tamaño (UTM), debes trasladar la nube de puntos al origen local restando el centroide antes de procesarlos.

Recuerda convertir siempre las unidades de metros a pies internos de Revit utilizando la clase de utilidad moderna:

double valorEnPies = UnitUtils.ConvertToInternalUnits(valorEnMetros, UnitTypeId.Meters);


3. Arquitectura de Hilos y Eventos Externos (External Events)

Seguridad de Hilos (Thread Safety): La API de Revit es de un solo hilo (Single-Threaded). Cualquier llamada a la API de Revit desde un hilo secundario (como una respuesta de un cliente HTTP asíncrono o un WebSocket en un panel WebView2) provocará un crash silencioso o una excepción de contexto de ejecución.

Implementación de IExternalEventHandler:

Registra el ExternalEvent únicamente durante el método OnStartup de tu IExternalApplication para evitar que el manejador quede huérfano.

Usa patrones de diseño concurrentes o bloqueos mutuos (lock) para pasar datos de manera segura entre el hilo de fondo (UI/Panel) y el manejador del evento.

Prohibición de Catch Vacíos: Bajo ninguna circunstancia uses bloques catch vacíos dentro de la implementación de IExternalEventHandler.Execute. Las excepciones de hilos secundarios no se propagan a la consola estándar de depuración de Visual Studio. Siempre encapsula la transacción en un try-catch y muestra el error de forma explícita usando un TaskDialog.Show o un logger persistente.

4. Estructura de Proyectos e Infraestructura .NET

Formato de Proyecto Moderno (SDK-Style): Para proyectos dirigidos a versiones contemporáneas de Revit que sigan utilizando .NET Framework 4.8 (como Revit 2024), prefiere el formato de archivo .csproj estilo SDK en lugar del formato clásico. Esto soluciona de raíz los problemas con la versión del compilador de C# (evitando errores como el CS8370 debido a tipos de referencia que aceptan valores nulos string?).

<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>8.0</LangVersion>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>


Fijar la versión del Lenguaje: Si estás forzado a usar un .csproj clásico, utiliza un archivo global de configuración Directory.Build.props en la raíz para sobreescribir las directivas restrictivas del SDK de MSBuild antiguo.

5. Gestión Eficiente de Recursos del Documento

Verificaciones de Existencia: No asumas que el documento del usuario contiene familias o niveles por defecto.

Si realizas una búsqueda de Level mediante un FilteredElementCollector y este devuelve vacío, crea un nivel por defecto programáticamente: Level.Create(doc, 0.0).

Verifica siempre la validez de los tipos cargados antes de iniciar una transacción para evitar que el add-in aborte de forma inesperada.