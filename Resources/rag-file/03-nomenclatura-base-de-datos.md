# Nomenclatura de base de datos — TNS Engineering

## Idioma
- **Todos los objetos de base de datos en español**
- Nombres de tablas, columnas, stored procedures, índices: español
- Excepción: palabras técnicas sin traducción natural (Id, Status, Hash)

## Tablas
- Nombre en singular y PascalCase: Cliente, Pedido, LineaPedido
- No usar prefijos de tipo (no tbl_, no T_)
- Tabla de relación N:M: combinar los dos nombres: ClienteProducto

## Columnas
- PascalCase: NombreCompleto, FechaCreacion, EstadoPedido
- Llave primaria siempre: Id (tipo UNIQUEIDENTIFIER, DEFAULT NEWID())
- Llave foránea: {TablaReferenciada}Id → ejemplo: ClienteId, ProductoId
- Fechas de auditoría obligatorias en toda tabla:
  - FechaCreacion DATETIME2 NOT NULL DEFAULT GETUTCDATE()
  - FechaActualizacion DATETIME2 NOT NULL DEFAULT GETUTCDATE()
  - CreadoPor NVARCHAR(100) NOT NULL
- Columnas de estado: usar INT con tabla de referencia, no varchar libre
- Columnas booleanas: prefijo "Es" o "Tiene" → EsActivo, TieneDescuento

## Índices
- Nombre: IX_{Tabla}_{Columna(s)} → IX_Pedido_ClienteId
- Índice único: UX_{Tabla}_{Columna} → UX_Cliente_CorreoElectronico
- Llave primaria: PK_{Tabla} → PK_Cliente

## Stored Procedures
- Prefijo del módulo + acción + entidad en español
  usp_Pedido_Crear
  usp_Cliente_ObtenerPorId
  usp_Reporte_VentasMensuales
- No usar SP para lógica de negocio compleja, solo para operaciones de datos

## Vistas
- Prefijo V_ + nombre descriptivo: V_PedidosActivos, V_ClientesConSaldo

## Constraints
- Check: CK_{Tabla}_{Columna} → CK_Producto_Precio
- Default: DF_{Tabla}_{Columna} → DF_Pedido_Estado

## Bases de datos por ambiente
- Desarrollo:  TNS_{Servicio}_Dev   → TNS_Pagos_Dev
- QA:          TNS_{Servicio}_QA    → TNS_Pagos_QA
- Producción:  TNS_{Servicio}_Prod  → TNS_Pagos_Prod