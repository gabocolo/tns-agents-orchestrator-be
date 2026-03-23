# Lineamientos de integración entre aplicaciones — TNS Engineering

## Principio general
Ninguna aplicación debe conectarse directamente a la base de datos 
de otra aplicación. Toda integración se hace por API o mensajería.

## Comunicación sincrónica (REST)
- Todas las APIs exponen contratos OpenAPI 3.0 (Swagger obligatorio)
- Versionamiento en la URL: /api/v1/, /api/v2/
- Autenticación: JWT Bearer token via Azure AD B2C
- Timeout máximo entre servicios internos: 30 segundos
- Reintentos: máximo 3 intentos con backoff exponencial
- Formato de fecha: ISO 8601 (yyyy-MM-ddTHH:mm:ssZ) siempre en UTC

## Comunicación asíncrona (Mensajería)
- Usar Azure Service Bus para eventos entre dominios
- Formato de mensaje: JSON con envelope estándar:
  {
    "messageId": "uuid",
    "timestamp": "ISO8601",
    "source": "nombre-servicio",
    "eventType": "NombreEvento",
    "version": "1.0",
    "payload": { ... }
  }
- Los consumidores deben ser idempotentes
- Dead Letter Queue obligatorio en todas las suscripciones

## Integración con sistemas externos
- Toda integración externa pasa por un servicio adaptador (Anti-Corruption Layer)
- No se permite consumir APIs externas directamente desde el frontend
- Las credenciales de sistemas externos viven en Azure Key Vault

## API Gateway
- Todas las APIs internas se exponen via Azure API Management
- El frontend solo conoce la URL del gateway, nunca las URLs internas
- Rate limiting configurado en el gateway: 1000 req/min por cliente

## Contratos de integración
- Los contratos (DTOs compartidos) se versionan en repositorio central
- Breaking changes requieren nuevo endpoint versionado, no modificar el existente
- Deprecación mínima: 6 meses de convivencia entre versiones