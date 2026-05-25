# Success Tickets - Entry Control

Aplicacion web ASP.NET Core MVC para controlar el ingreso a eventos mediante escaneo o escritura manual de codigos QR/tickets. El sistema autentica a un operador contra una API central, guarda la sesion en una cookie y usa el token recibido para validar tickets desde una pantalla protegida.

## Tabla de contenido

- [Que hace este proyecto](#que-hace-este-proyecto)
- [Tecnologias principales](#tecnologias-principales)
- [Arquitectura general](#arquitectura-general)
- [Flujo completo de trabajo](#flujo-completo-de-trabajo)
- [Estructura del proyecto](#estructura-del-proyecto)
- [Explicacion archivo por archivo](#explicacion-archivo-por-archivo)
- [Configuracion](#configuracion)
- [Como ejecutar localmente](#como-ejecutar-localmente)
- [Endpoints y rutas](#endpoints-y-rutas)
- [Contrato con la API central](#contrato-con-la-api-central)
- [Autenticacion y seguridad](#autenticacion-y-seguridad)
- [Frontend y experiencia de usuario](#frontend-y-experiencia-de-usuario)
- [Docker](#docker)
- [CI/CD con GitHub Actions](#cicd-con-github-actions)
- [NuGet y dependencias](#nuget-y-dependencias)
- [Carpetas generadas o de soporte](#carpetas-generadas-o-de-soporte)
- [Problemas conocidos y puntos a revisar](#problemas-conocidos-y-puntos-a-revisar)

## Que hace este proyecto

Success Tickets - Entry Control es una aplicacion para operadores de entrada en eventos. Su funcion principal es permitir que una persona encargada del acceso:

1. Inicie sesion con correo y contrasena.
2. Entre a una pantalla de control protegida.
3. Escanee un QR o escriba un codigo de ticket.
4. Envie ese codigo a una API central.
5. Reciba una respuesta de validacion.
6. Vea claramente si el ingreso esta permitido o rechazado.
7. Consulte datos del ticket cuando la API los devuelve: persona, evento, lugar, horario, silla, codigo y hora de revision.

La aplicacion no contiene la logica final del negocio de tickets. Esa responsabilidad vive en la API central configurada en `CentralApi`. Este proyecto funciona como interfaz web de escaneo y puente seguro entre el operador y la API.

## Tecnologias principales

- **ASP.NET Core MVC**: estructura principal de la aplicacion web.
- **.NET 10**: framework objetivo definido en `success.csproj`.
- **Razor Views**: vistas `.cshtml` para login, layout, error y pantalla de escaneo.
- **Cookie Authentication**: autenticacion local de la aplicacion despues de recibir el token de la API central.
- **HttpClientFactory**: clientes HTTP configurados para comunicarse con la API central.
- **Bootstrap, jQuery y jQuery Validation**: librerias estaticas presentes en `wwwroot/lib`.
- **Font Awesome CDN**: iconos usados en la interfaz.
- **Docker multi-stage build**: imagen de build con SDK y runtime liviano con ASP.NET.
- **GitHub Actions**: workflow para construir, publicar imagen en GHCR y desplegar en VPS.

## Arquitectura general

La aplicacion sigue una arquitectura MVC sencilla:

```text
Operador
   |
   v
Vista Razor + JavaScript
   |
   v
SuccessController
   |
   v
ITicketService
   |
   v
ApiTicketService
   |
   v
API central de tickets
```

Componentes principales:

- **Controladores**: reciben solicitudes HTTP, validan entrada basica y devuelven vistas o JSON.
- **Modelos**: definen datos de formularios, requests y responses.
- **Servicios**: encapsulan la comunicacion con la API central.
- **Vistas**: construyen la interfaz visual con Razor.
- **Assets estaticos**: CSS y JavaScript propios de la UI.
- **Configuracion**: define URL, paths, timeout y token opcional de la API central.

## Flujo completo de trabajo

### 1. Inicio de la aplicacion

`Program.cs` crea el builder de ASP.NET Core, lee configuracion desde `appsettings.json` y registra los servicios necesarios:

- `CentralApiOptions` desde la seccion `CentralApi`.
- `IHttpContextAccessor` para acceder al usuario actual desde servicios.
- `ITicketService` implementado por `ApiTicketService`.
- Cliente HTTP nombrado `CentralApi`.
- Autenticacion por cookies.
- Autorizacion.
- Controladores con vistas.

Luego configura el pipeline:

- En produccion usa manejador de errores y HSTS.
- Activa routing.
- Activa autenticacion y autorizacion.
- Sirve archivos estaticos.
- Mapea controladores y la ruta MVC por defecto.

La ruta por defecto es:

```text
/{controller=Success}/{action=Index}/{id?}
```

Eso significa que la pantalla principal es `Success/Index`.

### 2. Login del operador

El operador entra a:

```text
/Account/Login
```

`AccountController` muestra `Views/Account/Login.cshtml`. Al enviar el formulario:

1. ASP.NET valida el `LoginViewModel`.
2. Se hace POST a la API central:

```text
POST /api/auth/login
```

con:

```json
{
  "email": "operador@correo.com",
  "password": "password"
}
```

3. Si la respuesta no es exitosa, muestra un mensaje amigable.
4. Si la respuesta trae token, busca propiedades como `accessToken`, `token` o `jwt`.
5. Crea claims para la cookie:
   - nombre
   - email
   - access token
   - refresh token si existe
   - rol si existe
6. Firma al usuario con esquema `Cookies`.
7. Redirige al `ReturnUrl` si es local, o a `Success/Index`.

### 3. Pantalla de escaneo

`SuccessController` esta protegido con `[Authorize]`. Si el usuario no esta autenticado, sera enviado al login.

La vista `Views/Success/Index.cshtml` muestra:

- barra superior con nombre del modulo;
- reloj en vivo;
- formulario para escanear o escribir ticket;
- boton para limpiar;
- panel de resultado;
- detalles del ticket cuando existan.

El JavaScript de `wwwroot/js/site.js` intercepta el submit del formulario, toma el codigo y llama a:

```text
POST /Success/ValidateTicket
```

El request incluye token antiforgery y JSON:

```json
{
  "scanCode": "CODIGO_ESCANEADO"
}
```

### 4. Validacion del ticket

`SuccessController.ValidateTicket` revisa que `scanCode` exista. Si esta vacio devuelve error.

Si el codigo existe, llama:

```csharp
_ticketService.ValidateTicketAsync(request.ScanCode)
```

La implementacion real es `ApiTicketService`.

`ApiTicketService`:

1. Limpia el codigo.
2. Aplica header `Authorization: Bearer ...` usando el token de la sesion o `BearerToken` de configuracion.
3. Envia POST a la API central configurada:

```text
POST {CentralApi:ValidateTicketPath}
```

Por defecto:

```text
POST /api/scanner/validate
```

4. Envia un body con:

```json
{
  "qrCode": "CODIGO",
  "deviceInfo": "Web scanner - Nombre del operador"
}
```

5. Interpreta la respuesta de la API.
6. Convierte el resultado al formato que entiende la pantalla.
7. Devuelve un `TicketValidationResponse`.

### 5. Resultado visual

`site.js` recibe el JSON y actualiza la UI:

- Verde si `success = true`.
- Rojo si `success = false`.
- Spinner mientras espera.
- Mensaje de conexion si falla el fetch.
- Datos del ticket si vienen en la respuesta.

## Estructura del proyecto

```text
success/
├── Controllers/
│   ├── AccountController.cs
│   ├── HomeController.cs
│   └── SuccessController.cs
├── Models/
│   ├── Auth/
│   │   └── LoginViewModel.cs
│   ├── Responses/
│   │   ├── TicketValidationResponse.cs
│   │   └── ValidateTicketRequest.cs
│   └── ErrorViewModel.cs
├── Services/
│   ├── Interfaces/
│   │   └── ITicketService.cs
│   ├── ApiTicketService.cs
│   └── CentralApiOptions.cs
├── Views/
│   ├── Account/
│   │   └── Login.cshtml
│   ├── Shared/
│   │   ├── Error.cshtml
│   │   ├── _Layout.cshtml
│   │   ├── _Layout.cshtml.css
│   │   └── _ValidationScriptsPartial.cshtml
│   ├── Success/
│   │   ├── Index.cshtml
│   │   └── Privacy.cshtml
│   ├── _ViewImports.cshtml
│   └── _ViewStart.cshtml
├── wwwroot/
│   ├── css/
│   │   ├── layout.css
│   │   └── site.css
│   ├── js/
│   │   └── site.js
│   ├── lib/
│   │   ├── bootstrap/
│   │   ├── jquery/
│   │   ├── jquery-validation/
│   │   └── jquery-validation-unobtrusive/
│   └── favicon.ico
├── Properties/
│   └── launchSettings.json
├── .github/
│   └── workflows/
│       └── dotnet.yml
├── App_Data/
│   └── scan-activity.json
├── Program.cs
├── success.csproj
├── appsettings.json
├── appsettings.Development.json
├── Dockerfile
├── .dockerignore
├── .gitignore
└── README.md
```

## Explicacion archivo por archivo

### `Program.cs`

Punto de entrada de la aplicacion. Configura servicios, autenticacion, clientes HTTP, rutas MVC y pipeline de ASP.NET Core.

Responsabilidades:

- Leer la seccion `CentralApi`.
- Registrar `CentralApiOptions`.
- Registrar `IHttpContextAccessor`.
- Crear `HttpClient` tipado para `ITicketService`.
- Crear `HttpClient` nombrado `CentralApi` para login/logout.
- Configurar cookies:
  - login: `/Account/Login`
  - logout: `/Account/Logout`
  - acceso denegado: `/Account/Login`
- Registrar MVC.
- Mapear la ruta por defecto hacia `Success/Index`.

### `success.csproj`

Archivo de proyecto .NET. Define framework, paquetes NuGet y opciones de compilacion.

Configuracion principal:

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
```

Paquetes NuGet declarados:

- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` version `9.0.15`.
- `Microsoft.EntityFrameworkCore.Design` version `9.0.15`.
- `Pomelo.EntityFrameworkCore.MySql` version `9.0.0`.

Nota: en el codigo actual no se observa un `DbContext` ni uso directo de Entity Framework. Estos paquetes parecen preparados para integracion futura o quedaron de una version anterior.

### `appsettings.json`

Configuracion base de la aplicacion.

Seccion `CentralApi`:

- `BaseUrl`: URL base de la API central. Por defecto `http://localhost:5201`.
- `ValidateTicketPath`: endpoint de validacion. Por defecto `/api/scanner/validate`.
- `ValidateCodeProperty`: nombre esperado del campo de codigo. Actualmente esta configurado como `qrCode`.
- `TimeoutSeconds`: timeout del cliente HTTP. Por defecto `30`.
- `BearerToken`: token fijo opcional. Esta vacio por defecto.

Tambien configura logging y `AllowedHosts`.

### `appsettings.Development.json`

Configuracion para ambiente de desarrollo. Repite la seccion `CentralApi` y define niveles de logging. Se usa automaticamente cuando `ASPNETCORE_ENVIRONMENT=Development`.

### `Properties/launchSettings.json`

Define perfiles locales para ejecutar con Visual Studio, Rider o `dotnet run`.

Perfiles:

- `http`: abre `http://localhost:5110`.
- `https`: abre `https://localhost:7260` y `http://localhost:5110`.

Ambos usan:

```text
ASPNETCORE_ENVIRONMENT=Development
```

### `Dockerfile`

Define una imagen Docker multi-stage:

1. Usa `mcr.microsoft.com/dotnet/sdk:10.0` para restaurar, compilar y publicar.
2. Usa `mcr.microsoft.com/dotnet/aspnet:10.0` para ejecutar la app publicada.
3. Expone el puerto `8080`.
4. Ejecuta el ensamblado publicado con `dotnet`.

Nota importante: el `ENTRYPOINT` actual dice:

```dockerfile
ENTRYPOINT ["dotnet", "succes.dll"]
```

El proyecto se llama `success`, por lo que el DLL esperado normalmente seria `success.dll`. Esto puede romper el contenedor si el archivo publicado no se llama `succes.dll`.

### `.dockerignore`

Evita copiar archivos innecesarios al contexto de build Docker:

- `.git`
- `.idea`
- `bin`
- `obj`
- archivos de entorno
- archivos de Docker Compose
- `node_modules`
- `README.md`
- otros archivos de tooling

Esto reduce peso y evita incluir archivos sensibles o irrelevantes en la imagen.

### `.gitignore`

Archivo amplio para ignorar salidas de compilacion, configuraciones locales, archivos temporales y artefactos generados. Protege el repositorio de subir `bin/`, `obj/`, configuraciones locales del IDE y otros archivos no deseados.

### `Controllers/SuccessController.cs`

Controlador principal del modulo de escaneo. Tiene `[Authorize]`, por lo que exige usuario autenticado.

Rutas:

- `GET /`
- `GET /Success`
- `POST /Success/ValidateTicket`

Acciones:

- `Index()`: devuelve la vista de escaneo.
- `ValidateTicket(...)`: recibe el codigo escaneado, valida que no este vacio y llama a `ITicketService`.

Cuando falta el codigo devuelve:

```json
{
  "success": false,
  "title": "Code required",
  "message": "Scan the QR or type the ticket code.",
  "type": "error"
}
```

### `Controllers/AccountController.cs`

Controlador de autenticacion.

Rutas:

- `GET /Account/Login`
- `POST /Account/Login`
- `POST /Account/Logout`

Responsabilidades:

- Mostrar formulario de login.
- Validar modelo de login.
- Enviar credenciales a la API central.
- Leer token de la respuesta.
- Crear claims del usuario.
- Firmar la cookie local.
- Cerrar sesion local y opcionalmente avisar a la API central.

Metodos auxiliares:

- `IsLocalUrl`: evita redirecciones externas inseguras.
- `ReadMessage`: extrae mensajes comunes de errores JSON.
- `ToUserMessage`: reemplaza palabras tecnicas como `API` o `token` por mensajes mas entendibles.
- `ReadText`: busca propiedades en JSON sin depender de una forma exacta.
- `FindValue`: recorre objetos y arrays JSON para encontrar una propiedad por nombre.

### `Controllers/HomeController.cs`

Controlador usado para la pagina de error.

Accion:

- `Error()`: crea un `ErrorViewModel` con `RequestId` para soporte y diagnostico.

### `Services/Interfaces/ITicketService.cs`

Contrato del servicio de tickets.

Define:

```csharp
Task<TicketValidationResponse> ValidateTicketAsync(string scanCode);
```

Gracias a esta interfaz, el controlador no depende directamente de la implementacion HTTP. Esto permite reemplazar el servicio por mocks, pruebas o una implementacion diferente.

### `Services/ApiTicketService.cs`

Implementacion real de `ITicketService`. Se comunica con la API central.

Responsabilidades:

- Normalizar el codigo escaneado.
- Validar que no este vacio.
- Agregar header `Authorization`.
- Enviar request a la API central.
- Manejar errores HTTP, JSON, timeout y conexion.
- Mapear la respuesta externa a `TicketValidationResponse`.

Errores manejados:

- `Unauthorized` o `Forbidden`: estacion no autorizada.
- `JsonException`: respuesta no se pudo leer.
- `HttpRequestException`: no hay conexion con el sistema.
- `TaskCanceledException`: timeout.
- Respuesta sin datos: mensaje generico de fallo.

DTOs internos:

- `ValidateTicketApiRequest`: body enviado a la API central.
- `ScannerApiResponse`: envoltorio esperado de la API.
- `ValidateTicketApiResult`: resultado de validacion.
- `TicketDetailApiDto`: detalle del ticket recibido.

### `Services/CentralApiOptions.cs`

Clase que representa la configuracion `CentralApi`.

Propiedades:

- `BaseUrl`
- `ValidateTicketPath`
- `ValidateCodeProperty`
- `TimeoutSeconds`
- `BearerToken`

Se usa con el patron Options de ASP.NET Core.

### `Models/Auth/LoginViewModel.cs`

Modelo del formulario de login.

Campos:

- `Email`: obligatorio y con validacion de formato email.
- `Password`: obligatorio y marcado como password.
- `ReturnUrl`: URL local a donde volver despues de iniciar sesion.

Los mensajes de validacion estan en ingles:

- `Enter your email.`
- `Enter a valid email.`
- `Enter your password.`

### `Models/ErrorViewModel.cs`

Modelo para la vista de error.

Campos:

- `RequestId`: identificador de la solicitud.
- `ShowRequestId`: indica si se debe mostrar el identificador.

### `Models/Responses/ValidateTicketRequest.cs`

Modelo recibido por `POST /Success/ValidateTicket`.

Campo:

- `ScanCode`: codigo escaneado o escrito por el operador.

### `Models/Responses/TicketValidationResponse.cs`

Modelo JSON que el backend devuelve al frontend despues de validar.

Campos principales:

- `Success`: indica si el ingreso fue aprobado.
- `Title`: titulo corto del resultado.
- `Message`: descripcion para el operador.
- `Type`: tipo visual, por ejemplo `success` o `error`.
- `Ticket`: detalle opcional del ticket.

Campos de `TicketInfo`:

- `Code`
- `ClientName`
- `Email`
- `PhoneNumber`
- `EventName`
- `TicketType`
- `SeatNumber`
- `Row`
- `VenueName`
- `Status`
- `EntryMode`
- `ScanTime`
- `ShowtimeStart`
- `PhotoUrl`

No todos los campos se llenan actualmente desde `ApiTicketService`. Algunos quedan preparados para respuestas mas completas de la API.

### `Views/_ViewStart.cshtml`

Define el layout global:

```csharp
Layout = "_Layout";
```

Todas las vistas usan `Views/Shared/_Layout.cshtml` salvo que una vista indique lo contrario.

### `Views/_ViewImports.cshtml`

Importa namespaces y Tag Helpers de MVC:

- `@using Success`
- `@using Success.Models`
- `@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers`

Esto permite usar helpers como `asp-for`, `asp-action` y `partial`.

### `Views/Shared/_Layout.cshtml`

Layout principal HTML.

Incluye:

- `meta charset`
- viewport responsive
- titulo dinamico
- Font Awesome desde CDN
- `layout.css`
- `site.css`
- seccion opcional `Styles`
- sidebar fijo
- marca `Success Tickets`
- menu de navegacion
- boton de logout si el usuario esta autenticado
- enlace de login si no esta autenticado
- `<main class="page">` para renderizar la vista actual
- seccion opcional `Scripts`

### `Views/Shared/_Layout.cshtml.css`

CSS generado por plantilla ASP.NET para estilos base de layout, botones, enlaces, bordes y footer. En esta aplicacion el diseno principal esta realmente en `wwwroot/css/layout.css` y `wwwroot/css/site.css`.

### `Views/Shared/Error.cshtml`

Vista de error. Muestra:

- titulo `Could not load the page.`
- mensaje `Try again in a few moments.`
- codigo de soporte si existe `RequestId`.

### `Views/Shared/_ValidationScriptsPartial.cshtml`

Incluye scripts de validacion cliente:

- `jquery.validate.min.js`
- `jquery.validate.unobtrusive.min.js`

Se usa en el login para que las validaciones de `DataAnnotations` funcionen tambien en navegador.

### `Views/Account/Login.cshtml`

Vista del login.

Usa:

- `LoginViewModel`
- formulario `POST /Account/Login`
- antiforgery token
- input oculto `ReturnUrl`
- resumen de errores
- validacion por campo para email y password
- boton con icono de Font Awesome
- partial de scripts de validacion

### `Views/Success/Index.cshtml`

Vista principal de control de entrada.

Elementos importantes:

- `topbar`: titulo y reloj.
- `scanForm`: formulario de codigo.
- `@Html.AntiForgeryToken()`: token CSRF usado por JavaScript.
- `scanInput`: input para QR o codigo manual.
- `clearButton`: limpia el input.
- `resultCard`: panel donde aparece el resultado.
- `ticketDetails`: grilla con datos del ticket.
- `window.ticketUrls.validate`: URL generada por Razor para `ValidateTicket`.
- carga `~/js/site.js`.

### `Views/Success/Privacy.cshtml`

Vista sencilla de privacidad. Indica que la informacion de revision de tickets se usa solo para controlar entrada al evento.

### `wwwroot/js/site.js`

JavaScript principal de la pantalla de escaneo.

Funciones:

- Captura el submit del formulario.
- Valida que el codigo no este vacio.
- Envia `fetch` a `window.ticketUrls.validate`.
- Incluye el antiforgery token en el header `RequestVerificationToken`.
- Muestra estado de carga.
- Muestra resultado exitoso o error.
- Limpia y enfoca el input despues de validar.
- Cambia estado visual de conexion.
- Formatea fechas para mostrarlas al operador.
- Actualiza reloj en vivo cada segundo.
- Reemplaza terminos tecnicos por textos mas amigables.

### `wwwroot/css/layout.css`

CSS estructural de la aplicacion.

Define:

- variables de color;
- fondo general;
- sidebar;
- marca;
- menu;
- boton de logout;
- contenedor principal `.page`;
- responsive para pantallas menores a `800px`.

### `wwwroot/css/site.css`

CSS especifico de pantallas y componentes.

Define:

- topbar;
- reloj;
- login;
- paneles;
- formulario de escaneo;
- botones;
- estado online/offline;
- tarjetas de resultado;
- estados success/error/loading;
- detalles del ticket;
- responsive para dashboard y movil.

### `wwwroot/favicon.ico`

Icono del sitio mostrado por el navegador.

### `wwwroot/lib/bootstrap/*`

Libreria Bootstrap incluida localmente con CSS, JS, mapas fuente y licencia. Aunque el layout actual usa CSS propio, Bootstrap esta disponible para componentes o estilos futuros.

### `wwwroot/lib/jquery/*`

Libreria jQuery incluida localmente. Es necesaria para algunas integraciones de validacion unobtrusive.

### `wwwroot/lib/jquery-validation/*`

Plugin de validacion de formularios para jQuery.

### `wwwroot/lib/jquery-validation-unobtrusive/*`

Adaptador de validacion unobtrusive de ASP.NET Core. Conecta `DataAnnotations` del modelo con validaciones en el navegador.

### `.github/workflows/dotnet.yml`

Workflow de GitHub Actions llamado `Deploy production`.

Se ejecuta cuando hay push a:

```text
main
```

Tiene dos jobs:

1. `build-and-push`
   - hace checkout;
   - configura Docker Buildx;
   - cachea capas Docker;
   - inicia sesion en GitHub Container Registry;
   - construye la imagen;
   - publica `ghcr.io/${{ github.repository }}:latest`.

2. `deploy`
   - espera a `build-and-push`;
   - se conecta por SSH al VPS;
   - hace login en GHCR;
   - descarga la imagen latest;
   - detiene y elimina el contenedor anterior si existe;
   - ejecuta el nuevo contenedor en el puerto `8088:8080`.

Secrets usados:

- `SSH_KEY`
- `TOKEN`
- `GITHUB_TOKEN` provisto por GitHub Actions

### `App_Data/scan-activity.json`

Archivo local con entradas de actividad de escaneo. En el estado actual del codigo no se ve una lectura o escritura directa desde los controladores o servicios principales. Parece ser informacion historica, de prueba o soporte local.

Ejemplo de entrada:

```json
{
  "userKey": "scanner@tickets.com",
  "dateKey": "2026-05-22",
  "success": false,
  "name": "SUCCESS-DEMO-OK",
  "message": "Could not check ticket",
  "checkedAt": "2026-05-22T11:32:21.5888618-05:00"
}
```

## Configuracion

La configuracion mas importante esta en:

- `appsettings.json`
- `appsettings.Development.json`

Ejemplo:

```json
{
  "CentralApi": {
    "BaseUrl": "http://localhost:5201",
    "ValidateTicketPath": "/api/scanner/validate",
    "ValidateCodeProperty": "qrCode",
    "TimeoutSeconds": 30,
    "BearerToken": ""
  }
}
```

### Variables importantes

| Clave | Uso |
| --- | --- |
| `CentralApi:BaseUrl` | URL base de la API central. |
| `CentralApi:ValidateTicketPath` | Path que valida tickets. |
| `CentralApi:ValidateCodeProperty` | Nombre configurado para el campo de codigo. |
| `CentralApi:TimeoutSeconds` | Tiempo maximo de espera para llamadas HTTP. |
| `CentralApi:BearerToken` | Token fijo opcional si no se usa token de usuario. |

### Configuracion por variables de entorno

ASP.NET Core permite sobreescribir configuracion con variables de entorno usando doble guion bajo:

```bash
CentralApi__BaseUrl=http://api:5201
CentralApi__ValidateTicketPath=/api/scanner/validate
CentralApi__TimeoutSeconds=30
CentralApi__BearerToken=TOKEN
```

## Como ejecutar localmente

### Requisitos

- .NET SDK 10 instalado.
- API central corriendo y accesible.
- La URL de la API central configurada en `CentralApi:BaseUrl`.

Verificar version de .NET:

```bash
dotnet --version
```

Restaurar dependencias:

```bash
dotnet restore
```

Compilar:

```bash
dotnet build
```

Ejecutar:

```bash
dotnet run
```

Con el perfil local, la aplicacion usa:

```text
http://localhost:5110
https://localhost:7260
```

### Flujo minimo para probar

1. Levantar la API central en `http://localhost:5201`.
2. Confirmar que la API tenga endpoint de login `/api/auth/login`.
3. Ejecutar esta aplicacion.
4. Abrir `/Account/Login`.
5. Iniciar sesion con un operador valido.
6. Escanear o escribir un codigo en la pantalla principal.

## Endpoints y rutas

### Rutas MVC

| Metodo | Ruta | Controlador | Funcion |
| --- | --- | --- | --- |
| `GET` | `/` | `SuccessController.Index` | Pantalla principal de escaneo. |
| `GET` | `/Success` | `SuccessController.Index` | Pantalla principal de escaneo. |
| `POST` | `/Success/ValidateTicket` | `SuccessController.ValidateTicket` | Valida un codigo de ticket. |
| `GET` | `/Account/Login` | `AccountController.Login` | Muestra formulario de login. |
| `POST` | `/Account/Login` | `AccountController.Login` | Procesa login. |
| `POST` | `/Account/Logout` | `AccountController.Logout` | Cierra sesion. |
| `GET` | `/Home/Error` | `HomeController.Error` | Pagina de error. |

### Rutas externas esperadas en la API central

| Metodo | Ruta | Uso |
| --- | --- | --- |
| `POST` | `/api/auth/login` | Autenticar operador. |
| `POST` | `/api/logout` | Cerrar sesion en API central. |
| `POST` | `/api/scanner/validate` | Validar ticket escaneado. |

## Contrato con la API central

### Login

Request enviado:

```json
{
  "email": "operador@correo.com",
  "password": "password"
}
```

Respuesta esperada flexible. El controlador busca estas propiedades en cualquier nivel del JSON:

- token: `accessToken`, `token` o `jwt`
- refresh token: `refreshToken`
- nombre: `fullName`, `name`, `userName` o `email`
- rol: `role` o `roles`

### Validacion de ticket

Request enviado por `ApiTicketService`:

```json
{
  "qrCode": "CODIGO_ESCANEADO",
  "deviceInfo": "Web scanner - Nombre Operador"
}
```

Respuesta esperada:

```json
{
  "message": "Mensaje general",
  "data": {
    "isValid": true,
    "message": "Valid ticket. Access granted.",
    "ticket": {
      "ticketId": "123",
      "holderEmail": "cliente@correo.com",
      "eventName": "Evento",
      "venueName": "Lugar",
      "showtimeStart": "2026-05-22T20:00:00",
      "seatLabel": "A12",
      "wasAlreadyUsed": false,
      "usedAt": "2026-05-22T19:30:00"
    }
  }
}
```

Respuesta que esta app devuelve al navegador:

```json
{
  "success": true,
  "title": "Entry allowed",
  "message": "Valid ticket. Access granted.",
  "type": "success",
  "ticket": {
    "code": "123",
    "clientName": "cliente@correo.com",
    "email": "cliente@correo.com",
    "eventName": "Evento",
    "venueName": "Lugar",
    "seatNumber": "A12",
    "status": "",
    "scanTime": "2026-05-22T19:30:00",
    "showtimeStart": "2026-05-22T20:00:00"
  }
}
```

## Autenticacion y seguridad

### Cookies

La app usa autenticacion por cookies. Despues del login, el token de la API central se guarda como claim:

```text
access_token
```

Ese claim se usa despues para llamar la validacion de tickets con:

```text
Authorization: Bearer {token}
```

### Proteccion de rutas

`SuccessController` tiene `[Authorize]`, por lo que la pantalla de escaneo y la validacion requieren login.

### Antiforgery

Los formularios usan:

```csharp
@Html.AntiForgeryToken()
```

`ValidateTicket` tiene:

```csharp
[ValidateAntiForgeryToken]
```

El JavaScript envia el token en el header:

```text
RequestVerificationToken
```

### Redirecciones seguras

El login solo redirige a `ReturnUrl` si es una URL relativa local. Esto reduce riesgo de open redirect.

## Frontend y experiencia de usuario

La interfaz esta pensada para uso rapido en entrada de eventos:

- input con autofocus;
- permite scanner QR o escritura manual;
- boton de limpiar;
- resultado grande y visual;
- estados claros: listo, cargando, error, aprobado;
- reloj visible para operador;
- diseno responsive;
- mensajes tecnicos simplificados para usuario final.

Clases visuales principales:

- `.sidebar`
- `.brand`
- `.menu-link`
- `.page`
- `.topbar`
- `.dashboard`
- `.panel`
- `.scan-row`
- `.result-panel`
- `.ticket-details`
- `.auth-shell`
- `.auth-panel`

## Docker

Construir imagen:

```bash
docker build -t success-tickets .
```

Ejecutar contenedor:

```bash
docker run --rm -p 8080:8080 success-tickets
```

Con variables de entorno:

```bash
docker run --rm -p 8080:8080 \
  -e CentralApi__BaseUrl=http://host.docker.internal:5201 \
  -e CentralApi__ValidateTicketPath=/api/scanner/validate \
  success-tickets
```

Nota: antes de usar Docker en produccion, revisar el `ENTRYPOINT` del `Dockerfile`, porque actualmente apunta a `succes.dll`.

## CI/CD con GitHub Actions

El workflow de produccion hace:

1. Build de imagen Docker.
2. Push a GitHub Container Registry:

```text
ghcr.io/{owner}/{repo}:latest
```

3. Conexion SSH al VPS.
4. Pull de la imagen.
5. Reinicio del contenedor.
6. Publicacion del contenedor en:

```text
host:8088 -> container:8080
```

Variables/secrets necesarios:

- `SSH_KEY`: llave privada para entrar al VPS.
- `TOKEN`: token para hacer login en GHCR desde el VPS.
- `GITHUB_TOKEN`: usado por GitHub Actions para publicar paquetes.

## NuGet y dependencias

Dependencias declaradas en `success.csproj`:

| Paquete | Version | Uso esperado |
| --- | --- | --- |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | `9.0.15` | Integracion de Identity con Entity Framework. |
| `Microsoft.EntityFrameworkCore.Design` | `9.0.15` | Herramientas de diseno/migraciones EF Core. |
| `Pomelo.EntityFrameworkCore.MySql` | `9.0.0` | Provider MySQL/MariaDB para EF Core. |

Dependencias frontend incluidas en `wwwroot/lib`:

| Libreria | Uso |
| --- | --- |
| Bootstrap | CSS/JS base disponible. |
| jQuery | Base para validacion cliente. |
| jQuery Validation | Validacion de formularios. |
| jQuery Validation Unobtrusive | Integra DataAnnotations con validacion cliente. |

Dependencia externa por CDN:

```html
https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.1/css/all.min.css
```

Se usa para los iconos de QR, ticket, login, logout, check, error y loading.

## Carpetas generadas o de soporte

### `bin/`

Salida de compilacion local. No debe editarse manualmente ni subirse al repositorio.

### `obj/`

Archivos intermedios generados por .NET. No debe editarse manualmente ni subirse al repositorio.

### `.idea/`

Configuracion local del IDE JetBrains Rider/IntelliJ. Normalmente no se documenta como parte funcional de la app.

### `.agents/` y `.codex/`

Carpetas locales de herramientas/agentes presentes en el entorno de trabajo. No forman parte del runtime de la aplicacion.

## Problemas conocidos y puntos a revisar

1. **Posible error en Dockerfile**

   El `ENTRYPOINT` usa `succes.dll`, pero el proyecto se llama `success`. Si el DLL generado es `success.dll`, el contenedor fallara al iniciar.

2. **`ValidateCodeProperty` no se usa realmente**

   En configuracion existe `CentralApi:ValidateCodeProperty`, pero `ApiTicketService` envia siempre el campo `qrCode` por el record `ValidateTicketApiRequest`. Si se quiere hacer dinamico, habria que usar esa opcion al construir el JSON.

3. **Paquetes EF Core sin uso visible**

   El proyecto referencia paquetes de Identity y EF Core, pero no hay `DbContext`, migraciones ni acceso a base de datos en el codigo actual.

4. **`App_Data/scan-activity.json` no esta conectado**

   Existe un archivo con historial de escaneo, pero el codigo actual no parece leerlo ni escribirlo.

5. **Logout externo tolerante a fallos**

   Si `/api/logout` falla, la aplicacion igualmente cierra la sesion local. Esto es intencional para no bloquear al operador, pero significa que la sesion remota podria quedar activa dependiendo de la API central.

6. **Mensajes e interfaz en ingles**

   La UI y los mensajes internos estan mayormente en ingles. Si el publico operativo es hispanohablante, conviene traducir textos visibles.

## Resumen corto

Este proyecto es una interfaz web de control de entrada para Success Tickets. No administra eventos ni tickets directamente; consume una API central. Su mayor valor esta en ofrecer un flujo rapido y protegido para operadores: iniciar sesion, escanear codigo, validar contra la API y mostrar una decision clara de ingreso.
