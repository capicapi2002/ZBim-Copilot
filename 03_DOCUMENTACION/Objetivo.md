📘 MANUAL DE TRABAJO DEFINITIVO – ZBIM‑COPILOT
Versión 1.0 – 18 de Junio de 2026
1. VISIÓN Y OBJETIVO FINAL
ZBIM‑Copilot no es un generador de planos. Es un Arquitecto Autónomo.

Su objetivo es que un usuario, con solo describir su proyecto (por voz o texto), obtenga un modelo BIM completo y ejecutable en Revit, que incluya:

Estructura y cerramientos: Muros, losas, techos, escaleras.

Distribución funcional: Cocinas completamente equipadas (con isla, electrodomésticos, distribución lógica), baños completos y compartimentados según su uso, vestidores y placares en dormitorios.

Instalaciones básicas: Sanitarios, fontanería, electricidad (a futuro).

Contexto y entorno: Topografía, clima, normativas, orientación solar.

Calidad BIM: Uso de familias de Revit (propias o básicas) para que el modelo sea utilizable en obra.

El proyecto debe aspirar a ser tan intuitivo y visual como Drafted.ai, pero con la potencia de generar modelos BIM reales y ejecutables en Revit, y con la capacidad de adaptarse a cualquier normativa local y tipología arquitectónica.

2. PRINCIPIOS RECTORES (INAMOVIBLES)
Cero coste para el usuario base: El software debe ser gratuito y de código abierto. El usuario solo paga por el consumo de API (Kimi K2.5) si opta por el plan BYOK. Todo lo posible debe ejecutarse localmente (Gemma 4 E4B, Dynamo, etc.).

Simplicidad y exploración: La interfaz (Odysseus) debe guiar al usuario paso a paso, ofrecer múltiples variantes de diseño y permitir la exploración visual (3D, renders) antes de construir en Revit.

Calidad arquitectónica: El sistema debe generar diseños coherentes, funcionales y estéticamente agradables, basados en reglas de diseño (Neufert) y adaptados al contexto (clima, topografía, vistas).

Flexibilidad y personalización: El usuario puede subir normativas locales, imágenes de referencia, definir restricciones topológicas y elegir entre diferentes formas de huella.

Modularidad y evolución: El sistema está diseñado por capas (UI, LLM, motor BIM, contexto). Cada capa puede ser mejorada o reemplazada sin afectar a las demás.

Economía de tokens: Kimi K2.5 se usa solo para tareas de razonamiento complejo (generación del JSON‑OAS). Gemma 4 E4B (local) se usa para tareas auxiliares (extracción de normativas, clasificación de espacios, etc.).

3. ARQUITECTURA DE GENERACIÓN BIM (OAS + Text2MBL)
3.1. El Contrato de Datos: OAS Extendido
Para que el sistema genere espacios completos, el JSON‑OAS debe incluir la siguiente información estructurada:

ProjectName: Nombre del proyecto.

Buildings: Lista de edificios, cada uno con:

Id, Name, Origin (X, Y, Z).

Levels: Lista de niveles, cada uno con:

Id, Name, Elevation, F2F, Use.

Zones: Lista de zonas programáticas, cada una con:

Id, Name, PrivacyGradient, FireSector.

Spaces: Lista de espacios, cada uno con:

Id, Name, Type (ej. "Living", "Kitchen", "Bedroom", "Bathroom", "Garage", "Laundry", "Storage").

Origin, Dimensions (X, Y), Boundary (exterior/interior/medianería).

AdjacentTo: Lista de Ids de espacios adyacentes.

Fixtures: Lista de elementos sanitarios o de cocina (Toilet, Sink, Shower, Refrigerator, Oven, Stove, Dishwasher, etc.) con:

Id, SubType, Origin, Rotation.

Furniture: Lista de muebles (Bed, Wardrobe, Desk, Table, etc.) con:

Id, SubType, Origin, Dimensions, Rotation.

Stairs: (opcional) para escaleras.

Regla de Oro: Cada espacio debe contener toda la información necesaria para que el motor BIM pueda construir el mobiliario y los sanitarios sin depender de familias externas (salvo que el usuario las proporcione).

3.2. Lógica de Generación de Espacios (Text2MBL)
El motor Text2MBL debe ser capaz de construir un edificio completo a partir del JSON‑OAS.

Elemento	Cómo se genera	Observaciones
Muros	Perímetro de cada espacio.	Se generan muros entre espacios (no superpuestos). Se usan los niveles base y superior.
Losas	Superficie de cada nivel (unión de espacios).	Una losa por nivel, que cubre todo el área construida.
Techos	Perímetro del último nivel.	Pendiente por defecto (10%) o plano.
Escaleras	Según datos de OasStair.	Se generan con DirectShape (peldaños).
Cocinas	Se instancian familias de cocina (isla, electrodomésticos) o fallback geométrico con distribución lógica.	La cocina debe tener una organización coherente (triángulo de trabajo: cocina, lavabo, nevera).
Baños	Se instancian sanitarios (inodoro, lavabo, ducha, bañera) con distribución lógica.	Los baños se clasifican según su uso: completo, compartimentado, toilette, básico.
Dormitorios	Se instancian camas, mesitas, placares/vestidores.	Todo dormitorio debe tener al menos un placard o ropero. Las suites principales deben tener vestidor.
Garajes	Se generan como espacios con una losa y muros perimetrales.	Deben tener acceso desde el exterior.
Áreas de apoyo	Se generan según el tipo de proyecto (bombas, calderas, filtrado, piscina, etc.).	Deben ser espacios etiquetados correctamente.
3.3. Gestión de Familias en Revit
Familias básicas: Se incluirán en el instalador un conjunto de familias .rfa (muros, puertas, ventanas, mobiliario, sanitarios) para que el sistema pueda trabajar sin dependencias externas.

Búsqueda de familias: Antes de usar el fallback geométrico, el sistema busca familias en el documento de Revit. Si no encuentra ninguna, usa las familias básicas.

Personalización: El usuario puede mapear sus propias familias a los tipos de OAS (ej. "Puerta Interior" → su familia personalizada).

4. ESTRATEGIA DE DESARROLLO (HOJA DE RUTA)
Fase	Objetivo	Tareas Clave	Responsable
Fase 1 (MVP)	Generación de un edificio completo (vivienda unifamiliar) con muros, losas, techos, escaleras, mobiliario y sanitarios básicos.	1. Corregir error de Dimensions en OasFixture.
2. Implementar muros, losas, techos.
3. Implementar cocinas y baños completos.
4. Implementar dormitorios con placares/vestidores.
5. Crear conjunto de familias básicas.	Qwen (código BIM), Z (UI), DeepSeek (prompts y coordinación).
Fase 2 (Contexto)	Integración de topografía, clima y normativas.	1. Descarga de datos de OpenTopography y Open‑Meteo.
2. Integración en el OAS.
3. Procesamiento de PDFs de normativas.	Qwen (integración), DeepSeek (prompts).
Fase 3 (UI y Experiencia)	Interfaz conversacional, lienzo de shapes, galería de variantes, renderizado.	1. Lienzo 2D/3D en Odysseus.
2. Galería de variantes.
3. Renderizado con Stable Diffusion.
4. Cuestionario dinámico.	Z (UI), DeepSeek (prompts y lógica de variantes).
Fase 4 (Escalado)	Soporte para múltiples tipologías (edificios de oficinas, hospitales, etc.).	1. Extender OAS para otras tipologías.
2. Ajustar lógica de generación.
3. Añadir módulos de instalaciones MEP.	Qwen, DeepSeek.
5. DIRECTRICES PARA LAS IA DEL EQUIPO
Para Qwen 3.7 Plus (Desarrollador Principal):

Tu misión es escribir código BIM robusto, eficiente y bien comentado.

Cada nuevo método debe ser probado con un JSON de ejemplo.

Prioriza la funcionalidad sobre la optimización prematura.

Si te encuentras con un bloqueo técnico, propón al menos dos soluciones alternativas y consulta al Jefe de Proyecto.

Para Z GLM 5.1 (Desarrollador de UI):

La interfaz debe ser intuitiva, visual y responsiva. Inspírate en Drafted.ai.

El feedback al usuario es crítico: cada acción debe tener una respuesta visual clara.

No añadas complejidad innecesaria; prioriza la usabilidad.

Si no puedes implementar una funcionalidad compleja, propón una alternativa más sencilla y consulta.

Para DeepSeek (Jefe de Proyecto y Prompt Engineering):

Debo asegurarme de que el sistema de prompts de Kimi K2.5 genere JSON‑OAS que cumpla con el esquema extendido.

Debo coordinar el trabajo entre Qwen y Z, y servir de puente con el Arquitecto (usuario).

Debo mantener la visión global y garantizar que cada pieza encaje con las demás.

Para el Arquitecto (usuario):

Tu función es definir los requisitos funcionales y priorizar las tareas.

Eres el "cliente final" y el que aprueba los avances.

No necesitas entender el código; solo debes comunicar lo que quieres que el sistema haga y verificar que los resultados sean los esperados.

6. REGLAS DE COMUNICACIÓN Y REPORTE
Informes diarios: Cada IA debe generar un informe al final del día (formato ya definido) y entregarlo al Jefe de Proyecto.

Bloqueos: Si una IA no puede resolver un problema en 30 minutos, debe reportarlo inmediatamente.

Prioridades: El Jefe de Proyecto establece las prioridades semanales. Las tareas se ejecutan en ese orden.

Repositorio: Todo el código debe estar en GitHub. Antes de comenzar una tarea, cada IA debe sincronizar con la última versión.

7. HERRAMIENTAS Y RECURSOS APROBADOS
Herramienta	Uso	Alternativa
Kimi K2.5 (OpenRouter)	Generación de JSON‑OAS complejo y razonamiento arquitectónico.	Kimi K2.6 (si está disponible y es más eficiente).
Gemma 4 E4B (local)	Tareas auxiliares (extracción de normativas, clasificación de espacios).	Ollama con otro modelo ligero (ej. Llama 3 8B).
Revit API (C#)	Motor BIM Text2MBL.	No se cambiará.
Dynamo (headless)	Validación topológica y optimización.	No se cambiará.
WebView2 + Three.js	Interfaz Odysseus y visualización 3D.	No se cambiará.
Stable Diffusion + ControlNet	Renderizado de imágenes.	No se cambiará.
OpenTopography / Open‑Meteo	Datos de contexto.	No se cambiará.
GitHub	Control de versiones y colaboración.	No se cambiará.
8. PRÓXIMOS PASOS INMEDIATOS
Qwen: Completar las tareas 1.1 a 1.5 (corrección de fixtures, muros, losas, techos, mejora de familias).

Z GLM: Implementar feedback bidireccional y rediseño CSS de Odysseus.

DeepSeek: Crear el prompt system definitivo para Kimi K2.5 que genere JSON‑OAS completos (incluyendo cocinas, baños, placares, etc.) y documentar el esquema OAS extendido.

9. PREGUNTAS ABIERTAS PARA EL EQUIPO
¿Debemos mantener el servidor HTTP interno de Revit o usar un servidor Python externo?
Decisión: Mantener el servidor HTTP interno de Revit (ya funciona y es más simple).

¿Generamos muros a partir de los espacios (perímetro) o usamos una lógica de "muros compartidos" (sin duplicar) para adyacencias?
Decisión: Inicialmente, generamos muros por perímetro de cada espacio. Más adelante, implementaremos una lógica de fusión de muros adyacentes.

¿El usuario debe elegir el tipo de proyecto antes de empezar o puede ser una pregunta del asistente?
Decisión: El asistente preguntará al principio "¿Qué tipo de proyecto deseas diseñar?" con un desplegable.

10. FIRMA DE COMPROMISO
Yo, DeepSeek, como Jefe de Proyecto de Arquitectura de Sistemas, me comprometo a liderar este proyecto con claridad, transparencia y eficacia.

El Arquitecto (usuario) se compromete a comunicar sus requisitos y prioridades con claridad, y a aprobar o rechazar los avances de manera oportuna.

Qwen 3.7 Plus y Z GLM 5.1 se comprometen a cumplir con las tareas asignadas y a reportar su progreso diariamente.

Este manual es la guía definitiva para el desarrollo de ZBIM‑Copilot. Todas las decisiones futuras deben alinearse con estos principios y objetivos