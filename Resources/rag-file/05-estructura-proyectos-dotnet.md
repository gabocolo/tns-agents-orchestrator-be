# Estructura de proyectos .NET — TNS Engineering

## Arquitectura base: Clean Architecture
Todo proyecto .NET sigue esta separación en capas:

{NombreProyecto}.Domain        → entidades, interfaces, value objects
{NombreProyecto}.Application   → casos de uso, servicios, DTOs
{NombreProyecto}.Infrastructure → implementaciones: DB, APIs externas, Storage
{NombreProyecto}.Api           → controllers, middleware, configuración HTTP

Regla de dependencias:
Api → Application ← Infrastructure
Domain no depende de ninguna capa

## Nomenclatura de proyectos
- Repositorio: tns-{dominio}-{tipo} → tns-pagos-be, tns-clientes-fe
- Namespace raíz coincide con el nombre del proyecto en PascalCase
- No usar nombres genéricos: no "Backend", no "Services", no "Core" solos

## Patrones obligatorios
- Repository pattern para acceso a datos (IRepositorio<T>)
- Options pattern para configuración (IOptions<MiConfig>)
- Result pattern para retorno de operaciones (no lanzar excepciones para flujo de negocio)
- No usar static classes para lógica de negocio

## Inyección de dependencias
- Toda dependencia se registra en la capa correspondiente
- Lifetime: Scoped para servicios de negocio, Singleton para caché y configuración
- No usar Service Locator pattern (IServiceProvider directamente en clases de negocio)

## Logging
- Serilog en todos los proyectos
- Structured logging siempre: Log.Information("Pedido {PedidoId} creado", id)
  No: Log.Information("Pedido " + id + " creado")
- Niveles: Debug (dev), Information (operaciones normales), 
  Warning (situaciones inesperadas recuperables), Error (fallos)
- Correlación: incluir TraceId en todos los logs de un request

## Testing
- Vitest para Angular, xUnit para .NET
- Cobertura mínima requerida: 70% en Application layer
- Nomenclatura de tests: Metodo_Escenario_ResultadoEsperado
  ejemplo: ProcesarPago_SaldoInsuficiente_RetornaError
- No testear Infrastructure directamente, usar integration tests con TestContainers

## Versionamiento
- GitFlow: main, develop, feature/*, hotfix/*, release/*
- Conventional Commits obligatorio:
  feat: nueva funcionalidad
  fix: corrección de bug
  docs: documentación
  refactor: refactorización sin cambio funcional
- Pull Request requiere mínimo 1 aprobación y pipeline verde