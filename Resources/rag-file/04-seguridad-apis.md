# Lineamientos de seguridad en APIs — TNS Engineering

## Autenticación y autorización
- JWT Bearer obligatorio en todos los endpoints excepto /health y /swagger
- Tokens emitidos por Azure AD B2C — no implementar auth propio
- Expiración de token de acceso: 1 hora máximo
- Refresh token: 8 horas, renovación silenciosa en el cliente
- Claims obligatorios en el token: sub, email, roles, tenant_id

## HTTPS
- TLS 1.2 mínimo, preferir TLS 1.3
- No permitir HTTP en ningún ambiente incluyendo desarrollo
- HSTS habilitado en todos los servicios expuestos

## Headers de seguridad obligatorios
Todos los responses deben incluir:
- X-Content-Type-Options: nosniff
- X-Frame-Options: DENY
- Content-Security-Policy: default-src 'self'
- Referrer-Policy: no-referrer

## Manejo de errores
- Nunca exponer stack traces en respuestas de producción
- Nunca exponer nombres de tablas, columnas o queries en errores
- Formato estándar de error:
  {
    "error": "descripcion_tecnica_sin_datos_internos",
    "traceId": "uuid-para-correlacion-en-logs",
    "timestamp": "ISO8601"
  }

## Datos sensibles
- Passwords: bcrypt con cost factor 12 mínimo, nunca MD5/SHA1
- Tarjetas de crédito: nunca almacenar, usar token del proveedor de pagos
- PII (datos personales): cifrado en reposo con Azure Key Vault
- Logs: nunca loguear passwords, tokens, números de tarjeta o datos PII
- Connection strings: solo en Azure Key Vault, nunca en appsettings.json

## Validación de inputs
- Validar y sanear todos los inputs en el servidor, nunca confiar en el cliente
- Tamaño máximo de payload: 10 MB
- Rate limiting por IP y por usuario autenticado
- SQL Injection: usar solo parámetros, nunca concatenar queries

## Secretos y configuración
- Azure Key Vault para todos los secretos en QA y Prod
- En desarrollo: .NET User Secrets o variables de entorno locales
- Prohibido: secretos en código fuente, en appsettings.json 
  o en variables de entorno de pipelines en texto plano