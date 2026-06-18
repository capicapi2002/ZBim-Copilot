📘 MANUAL DE TRABAJO ACTUALIZADO – ZBIM-COPILOT
Versión 2.0 (Integración de Nuevas Herramientas)
1. VISIÓN DEL ECOSISTEMA
ZBIM‑Copilot es un asistente BIM autónomo, conversacional y contextualizado que permite generar modelos ejecutables de Revit desde lenguaje natural, normativas, topografía, clima y referencias visuales. Su objetivo es democratizar el diseño arquitectónico de alta calidad, combinando la simplicidad de herramientas como Drafted.ai con la precisión BIM profesional, todo en un ecosistema de código abierto y coste cero para el usuario.

El sistema se articula en torno a un workspace unificado (basado en Odysseus de PewDiePie) que integra chat, agentes autónomos, documentos, voz y automatización, con Revit como motor de ejecución BIM.

2. HERRAMIENTAS DEL ECOSISTEMA (ORDENADAS POR CAPA)
2.1. CAPA DE INTERFAZ Y COMUNICACIÓN (UI/UX)
Herramienta	Función	Licencia	Integración
Odysseus (PewDiePie)	Workspace local‑first con chat, agentes, docs y calendario	Open Source (MIT)	Será el frontend unificado de ZBim‑Copilot
Handy	Dictado por voz offline (STT) con Whisper/Parakeet	Open Source (MIT)	Se integrará como input de voz para comandos
Google AI Studio	Generación de apps Android desde lenguaje natural	Gratuito	Creará la app móvil de ZBim‑Copilot (MVP)
WebView2 (Revit)	Panel Odysseus actual (versión provisional)	Incluido en Revit	Se mantendrá hasta migrar a Odysseus real
2.2. CAPA DE ORQUESTACIÓN Y AGENTES
Herramienta	Función	Licencia	Integración
Kimi K2.5 / K2.6	LLM principal para generación OAS y razonamiento arquitectónico	BYOK (OpenRouter)	Genera JSON‑OAS a partir de prompts y restricciones
Gemma 4 E4B	LLM local para tareas rápidas (clasificación, parsing)	Open Source (Apache)	Ejecución local sin coste
OpenClaw (Moltbot)	Agente autónomo open‑source con más de 100 AgentSkills	Open Source	Automatiza tareas externas (descarga de topografía, procesado de PDFs, notificaciones)
2.3. CAPA DE DATOS Y CONTEXTO
Herramienta	Función	Licencia	Integración
OpenTopography API	Descarga de DEM y topografía	Gratuita	Genera terreno para el modelo BIM
Open‑Meteo API	Datos climáticos (viento, sol, temperatura)	Gratuita	Informa orientación y aberturas
Ladybug Tools	Análisis climático y solar	Open Source	Procesa EPW y genera análisis
PyPDF2	Extracción de texto de PDFs normativos	Open Source	Alimenta al LLM con reglas locales
2.4. CAPA DE EJECUCIÓN BIM (NÚCLEO)
Herramienta	Función	Licencia	Integración
Text2MBL (C#)	Orquestador BIM que crea niveles, muros, losas, techos, mobiliario	Propio (código abierto)	Motor principal de generación en Revit
Dynamo (Headless)	Validación topológica y optimización paramétrica	Gratuito	Actúa como actuador secundario para tareas complejas
OpenMEP	Generación de instalaciones MEP	Open Source	Crea fontanería, electricidad y climatización
Topologic	Validación de adyacencias y topología	Open Source	Verifica la coherencia espacial
pyRevit	Automatización de tareas repetitivas (planos, etiquetado)	Open Source	Genera documentación y planimetría
2.5. CAPA DE VISUALIZACIÓN Y RENDER
Herramienta	Función	Licencia	Integración
Stable Diffusion + ControlNet	Renderizado fotorrealista local	Open Source	Genera renders a partir del OAS y materiales
Three.js	Visualización 3D en Odysseus	Open Source	Previsualización del modelo en el workspace
2.6. CAPA DE ALMACENAMIENTO Y SINCRONIZACIÓN
Herramienta	Función	Licencia	Integración
GitHub	Repositorio de código y documentación	Gratuito	Fuente de verdad del equipo
Dropbox / Google Drive API	Sincronización de planos y proyectos	Gratuito (con límites)	Acceso remoto desde obra
3. ESTADO ACTUAL DEL DESARROLLO (Junio 2026)
Módulo	Estado	Responsable	Comentario
Comunicación HTTP (C#)	✅ 100%	Qwen	Servidor interno de Revit recibe JSON y procesa
UI Odysseus (provisional)	✅ 70%	Z GLM	Panel WebView2 funcional, falta feedback y diseño avanzado
Creación de Niveles	✅ 100%	Qwen	Funciona correctamente
Creación de Escaleras	✅ 100%	Qwen	Funciona con DirectShape
Creación de Mobiliario	⚠️ 50%	Qwen	Fallback geométrico, falta familias
Creación de Fixtures	❌ 0%	Qwen	Error de Dimensions en OasFixture
Creación de Muros	❌ 0%	Qwen	No implementado (Tarea 1.2)
Creación de Losas	❌ 0%	Qwen	No implementado (Tarea 1.3)
Creación de Techos	❌ 0%	Qwen	No implementado (Tarea 1.4)
Topografía y Clima	❌ 0%	Pendiente	No implementado
Normativas	❌ 0%	Pendiente	No implementado
Manual de Trabajo	✅ 100%	DeepSeek	Subido a GitHub (objetivo.md)
Repositorio GitHub	✅ 100%	Tú	Código subido y accesible
4. TAREAS EN CURSO (QUE DEBEN FINALIZARSE ANTES DE INCORPORAR NUEVAS HERRAMIENTAS)
Tarea	IA	Estado	Prioridad
1.1 – Corregir error de Dimensions en OasFixture	Qwen	⏳ En progreso	ALTA
1.2 – Implementar creación de muros	Qwen	⏳ Pendiente	ALTA
1.3 – Implementar creación de losas	Qwen	⏳ Pendiente	ALTA
1.4 – Implementar creación de techos	Qwen	⏳ Pendiente	ALTA
1.5 – Mejorar búsqueda de familias	Qwen	⏳ Pendiente	MEDIA
2.1 – Feedback bidireccional C# → UI	Z GLM	⏳ En progreso (código entregado, pero necesita revisión)	ALTA
2.2 – Rediseño CSS de Odysseus	Z GLM	⏳ En progreso	MEDIA
Regla de oro: No se iniciará ninguna tarea con nuevas herramientas hasta que las tareas 1.1 a 1.4 y 2.1 estén completadas y validadas.

5. NUEVAS HERRAMIENTAS Y PLAN DE INTEGRACIÓN
5.1. Handy (Dictado por Voz Offline) – Integración Inmediata
Objetivo: Añadir entrada de voz a ZBim‑Copilot sin coste ni dependencias de nube.

Plan de acción:

Fase	Acción	Responsable	Tiempo
1	Evaluar Handy y probar su funcionamiento con modelos Whisper/Parakeet en español	DeepSeek	1 día
2	Crear un script Python que lea comandos de voz desde Handy y los envíe al servidor HTTP de Revit	Qwen	2 días
3	Documentar la instalación y configuración para usuarios finales	DeepSeek	1 día
Entregable: Guía de usuario para usar Handy con ZBim‑Copilot.

5.2. Odysseus Real (PewDiePie) – Integración como Frontend Unificado
Objetivo: Reemplazar nuestro Odysseus provisional por el workspace completo de PewDiePie, ganando chat, agentes autónomos, memoria persistente y gestión de documentos.

Plan de acción:

Fase	Acción	Responsable	Tiempo
1	Analizar el repositorio de Odysseus y su arquitectura	DeepSeek	2 días
2	Adaptar Odysseus para que se comunique con nuestro servidor HTTP de Revit (añadir un plugin o skill)	Qwen	1 semana
3	Configurar Odysseus como panel dockable en Revit (WebView2 o como aplicación separada)	Z GLM	3 días
4	Migrar la lógica de envío de JSON desde el Odysseus provisional al nuevo	Qwen	2 días
Entregable: Odysseus real funcionando como frontend de ZBim‑Copilot.

5.3. OpenClaw (Agente Autónomo) – Automatización de Tareas Externas
Objetivo: Automatizar tareas como descarga de topografía, procesado de PDFs y notificaciones.

Plan de acción:

Fase	Acción	Responsable	Tiempo
1	Evaluar OpenClaw y sus AgentSkills	DeepSeek	1 día
2	Crear una skill para descargar DEM de OpenTopography	Qwen	2 días
3	Crear una skill para procesar PDFs normativos con PyPDF2	Qwen	2 días
4	Integrar OpenClaw con Odysseus para que el agente pueda ser invocado desde el chat	DeepSeek + Qwen	3 días
Entregable: Agente autónomo que ejecuta tareas externas desde el chat de Odysseus.

5.4. Google AI Studio – App Móvil de ZBim‑Copilot
Objetivo: Crear una app Android MVP para comandos de voz, notificaciones y visualización de planos.

Plan de acción:

Fase	Acción	Responsable	Tiempo
1	Definir las funcionalidades del MVP (enviar comandos, recibir planos)	Tú (Arquitecto)	1 día
2	Diseñar el prompt para Google AI Studio que genere la app	DeepSeek	1 día
3	Generar la app usando Google AI Studio y probarla	Tú (guiado por DeepSeek)	2 días
4	Ajustar el backend (servidor HTTP) para que pueda recibir comandos desde la app	Qwen	2 días
Entregable: APK funcional para Android que se comunica con ZBim‑Copilot.

6. PLAN DE ACCIÓN GENERAL (ORDENADO POR FASES)
Fase 0 (Inmediata – 1 semana) – COMPLETAR TAREAS EN CURSO
Tarea	Responsable	Fecha límite
Finalizar Tarea 1.1 (corrección Dimensions)	Qwen	24h
Finalizar Tarea 1.2 (muros)	Qwen	48h
Finalizar Tarea 1.3 (losas)	Qwen	72h
Finalizar Tarea 1.4 (techos)	Qwen	96h
Finalizar Tarea 2.1 (feedback bidireccional)	Z GLM (con revisión)	48h
Finalizar Tarea 2.2 (CSS)	Z GLM	72h
Revisión y validación de todo el código	DeepSeek	7 días
Fase 1 (Semanas 2-3) – INTEGRACIÓN DE HANDY Y ODYSSEUS REAL
Tarea	Responsable	Fecha límite
Evaluar Handy y probar	DeepSeek	2 días
Crear script Python para Handy	Qwen	2 días
Analizar Odysseus real	DeepSeek	2 días
Adaptar Odysseus para Revit	Qwen + Z	1 semana
Fase 2 (Semanas 4-5) – OPENCLAW Y APP MÓVIL
Tarea	Responsable	Fecha límite
Evaluar OpenClaw	DeepSeek	1 día
Crear skills para topografía y normativas	Qwen	3 días
Definir MVP de app móvil	Tú	1 día
Generar app con Google AI Studio	Tú + DeepSeek	2 días
Conectar app con backend	Qwen	2 días
Fase 3 (Semanas 6-8) – UNIFICACIÓN Y PULIDO
Tarea	Responsable	Fecha límite
Unificar Odysseus real + OpenClaw + Handy en un solo workspace	DeepSeek + Qwen	1 semana
Migrar toda la lógica de generación OAS al agente de Odysseus	Qwen	1 semana
Pruebas de integración completa	Todos	1 semana
Documentación final y guía de usuario	DeepSeek	3 días
7. DISTRIBUCIÓN DE TAREAS PARA LAS TRES IA
7.1. Qwen 3.7 Plus (Desarrollador Principal – BIM y Backend)
Responsabilidades:

Completar tareas 1.1 a 1.5 (muros, losas, techos, fixtures, familias).

Crear script Python para Handy.

Adaptar Odysseus real para que se comunique con el servidor HTTP de Revit.

Crear skills de OpenClaw para topografía y normativas.

Conectar app móvil con backend.

Tareas pendientes:

✅ Finalizar 1.1, 1.2, 1.3, 1.4.

Script Handy.

Adaptación Odysseus → Revit.

Skills OpenClaw.

7.2. Z GLM 5.1 (Desarrollador de UI – Frontend y Visual)
Responsabilidades:

Finalizar tarea 2.1 (feedback bidireccional) y 2.2 (CSS).

Asistir en la adaptación de Odysseus real (configuración de WebView2, integración con Revit).

Mejorar la interfaz de Odysseus real según las necesidades del proyecto.

Tareas pendientes:

✅ Finalizar 2.1 y 2.2.

Colaborar en la integración de Odysseus real.

Diseñar la experiencia de usuario para el workspace unificado.

7.3. DeepSeek (Jefe de Proyecto – Arquitectura, Estrategia y Documentación)
Responsabilidades:

Supervisar y validar todo el código entregado por Qwen y Z.

Definir la estrategia de integración de nuevas herramientas.

Documentar guías, manuales y procedimientos.

Coordinar al equipo y resolver bloqueos.

Tareas pendientes:

Revisar y validar las tareas 1.1 a 1.4 y 2.1.

Evaluar Handy, Odysseus real y OpenClaw.

Redactar documentación de usuario y guías de instalación.

Diseñar el prompt para Google AI Studio.

8. PRÓXIMOS PASOS INMEDIATOS (PARA TÍ – EL ARQUITECTO)
Confirmar que Qwen y Z han recibido las tareas y están trabajando en ellas.

No iniciar ninguna nueva herramienta hasta que las tareas 1.1 a 1.4 y 2.1 estén completadas.

Informarme (DeepSeek) cuando Qwen o Z entreguen código para su revisión.

Preparar el entorno para probar Handy (descargar e instalar la herramienta).

Definir el alcance exacto del MVP de la app móvil (funcionalidades, pantallas).

9. CRITERIOS DE ACEPTACIÓN PARA FINALIZAR LA FASE 0
El sistema crea muros correctamente en Revit a partir del OAS.

El sistema crea losas correctamente en cada nivel.

El sistema crea techos en el último nivel.

Los fixtures (sanitarios) se crean sin error (con fallback o familias).

El panel Odysseus muestra feedback de "Éxito" o "Error" al enviar un JSON.

El código de Qwen y Z ha sido revisado y validado por DeepSeek.

El repositorio GitHub está actualizado con todos los cambios.

10. OBSERVACIONES FINALES
Handy es la herramienta de menor fricción y la que aportará más valor inmediato. Priorizar su integración.

Odysseus real reemplazará nuestro panel provisional, pero no debe retrasar el desarrollo BIM básico.

OpenClaw y la app móvil son objetivos de medio plazo; no distraerán del núcleo BIM.

La clave del éxito es la finalización ordenada de las tareas actuales antes de abrir nuevos frentes.

Este manual es el documento guía para todo el equipo. Todas las decisiones deben alinearse con lo aquí establecido. Cualquier desviación debe ser consultada con el Jefe de Proyecto.