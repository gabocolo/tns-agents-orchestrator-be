# Nomenclatura de base de datos — TNS Engineering
## Idioma
- **Todos los objetos de base de datos en español**
- Nombres de tablas, columnas, stored procedures, índices: español
- Excepción: palabras técnicas sin traducción natural (Id, Status, Hash)

## Tablas
- Prefijo obligatorio `tbl` + nombre en singular y PascalCase: `tblCliente`, `tblPedido`, `tblLineaPedido`
- Tabla de relación N:M: `tbl` + combinación de los dos nombres: `tblClienteProducto`

## Columnas
- PascalCase: `NombreCompleto`, `FechaCreacion`, `EstadoPedido`
- Llave primaria siempre: `Id` (tipo `UNIQUEIDENTIFIER`, `DEFAULT NEWID()`)
- Llave foránea: `{TablaReferenciada}Id` → ejemplo: `ClienteId`, `ProductoId`
- Fechas de auditoría obligatorias en toda tabla:
  - `FechaCreacion DATETIME2 NOT NULL DEFAULT GETUTCDATE()`
  - `FechaActualizacion DATETIME2 NOT NULL DEFAULT GETUTCDATE()`
  - `CreadoPor NVARCHAR(100) NOT NULL`
- Columnas de estado: usar `INT` con tabla de referencia, no varchar libre
- Columnas booleanas: prefijo `Es` o `Tiene` → `EsActivo`, `TieneDescuento`

## Índices
- Nombre: `IX_{Tabla}_{Columna(s)}` → `IX_tblPedido_ClienteId`
- Índice único: `UX_{Tabla}_{Columna}` → `UX_tblCliente_CorreoElectronico`
- Llave primaria: `PK_{Tabla}` → `PK_tblCliente`

## Stored Procedures
- Prefijo `sp` + módulo + acción + entidad en español:
```
  spPedido_Crear
  spCliente_ObtenerPorId
  spReporte_VentasMensuales
```
- No usar SP para lógica de negocio compleja, solo para operaciones de datos

## Vistas
- Prefijo `V_` + nombre descriptivo: `V_PedidosActivos`, `V_ClientesConSaldo`

## Constraints
- Check: `CK_{Tabla}_{Columna}` → `CK_tblProducto_Precio`
- Default: `DF_{Tabla}_{Columna}` → `DF_tblPedido_Estado`

## Bases de datos por ambiente
```
Desarrollo:  TNS_{Servicio}_Dev   → TNS_Pagos_Dev
QA:          TNS_{Servicio}_QA    → TNS_Pagos_QA
Producción:  TNS_{Servicio}_Prod  → TNS_Pagos_Prod
```