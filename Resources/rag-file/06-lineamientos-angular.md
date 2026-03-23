# Lineamientos Angular — TNS Engineering

## Versión y configuración
- Angular 17+ con Standalone Components obligatorio (no NgModules)
- Signals para state management (no NgRx para proyectos nuevos)
- Lazy loading en todas las rutas de features
- Strict mode habilitado en tsconfig.json

## Estructura de carpetas
src/app/
├── core/          # Servicios singleton, guards, interceptores
├── features/      # Un módulo por dominio de negocio
│   └── {feature}/
│       ├── pages/        # Componentes de página (routed)
│       ├── components/   # Componentes presentacionales
│       ├── services/     # Servicios del feature
│       └── models/       # Interfaces y tipos
├── layout/        # Header, Sidebar, Shell
└── shared/        # Componentes y pipes reutilizables entre features

## Componentes
- Standalone: true en todos los componentes
- ChangeDetection: OnPush obligatorio en componentes presentacionales
- Inputs con signal: input() en lugar de @Input() para componentes nuevos
- Outputs: output() en lugar de @Output() EventEmitter para componentes nuevos
- No lógica de negocio en componentes, delegar a servicios

## Servicios
- providedIn: 'root' para servicios singleton
- Signals para estado reactivo: signal(), computed()
- Evitar BehaviorSubject cuando un Signal es suficiente
- HTTP calls solo en servicios, nunca en componentes

## Nomenclatura
- Componentes: {nombre}.component.ts → PascalCase en clase
- Servicios: {nombre}.service.ts
- Guards: {nombre}.guard.ts
- Interceptores: {nombre}.interceptor.ts
- Modelos/interfaces: prefijo I no requerido, sufijo Model o tipo descriptivo
- Signals: camelCase sin sufijo especial → isLoading, proposals, selectedItem

## Estilos
- Angular Material como librería de componentes UI
- SCSS para estilos, no CSS plano
- Variables CSS para colores y espaciados del design system
- No usar estilos inline en templates
- Encapsulation: Emulated (default), no usar None salvo excepciones justificadas

## Performance
- trackBy obligatorio en todos los *ngFor / @for
- Imágenes con NgOptimizedImage
- Bundle size: alertar si un chunk lazy supera 500KB
- No importar librerías completas: 
  import { specific } from 'library' no import * from 'library'

## Seguridad en templates
- No usar innerHTML sin sanitización
- No deshabilitar DomSanitizer sin revisión de seguridad
- Nunca interpolar URLs directamente, usar routerLink o UrlTree
```

---

## Cómo subirlos a Azure AI Search

Tienes dos caminos. El más simple para una POC:

**Opción A — Azure Portal (sin código, 10 minutos)**
```
1. Ve a tu recurso Azure AI Search en el portal
2. Menú izquierdo → "Import data"
3. Data source → "Azure Blob Storage"
4. Crea un Storage Account nuevo (o usa uno existente)
5. Crea un contenedor llamado: "architecture-guidelines"
6. Sube los 6 archivos .md ahí
7. El wizard de "Import data" crea el índice automáticamente