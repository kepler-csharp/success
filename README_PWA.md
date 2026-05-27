# README PWA - Success Tickets

Este documento explica como esta implementada la PWA en este proyecto ASP.NET Core MVC y que pasos seguir para mantenerla o replicarla.

## Que es una PWA en este proyecto

Una PWA, o Progressive Web App, permite que la aplicacion web se comporte parecido a una app instalada:

- se puede instalar desde el navegador;
- puede abrir en modo `standalone`, sin verse como una pestana normal;
- mantiene archivos estaticos en cache;
- muestra una pagina offline cuando no hay conexion;
- usa iconos, nombre, color de tema y configuracion definida en un manifest.

En Success Tickets la PWA sirve para que el operador pueda abrir rapido la pantalla de control de ingreso. La validacion de tickets sigue necesitando internet porque depende de la API central.

## Archivos usados

La PWA esta hecha con estos archivos:

```text
wwwroot/
+-- manifest.webmanifest
+-- service-worker.js
+-- offline.html
+-- favicon.ico
+-- icons/
|   +-- icon.svg
|   +-- maskable-icon.svg
+-- css/
|   +-- layout.css
|   +-- site.css
+-- js/
    +-- site.js
+-- lib/
    +-- html5-qrcode/
        +-- html5-qrcode.min.js
```

Tambien se conecta desde:

```text
Views/Shared/_Layout.cshtml
```

## Paso 1: Crear el manifest

Archivo:

```text
wwwroot/manifest.webmanifest
```

El manifest le dice al navegador como instalar la app:

```json
{
  "name": "Success Tickets - Entry Control",
  "short_name": "Success Tickets",
  "description": "Control de ingreso para eventos mediante escaneo y validacion de tickets.",
  "start_url": "/",
  "scope": "/",
  "display": "standalone",
  "background_color": "#f8fafc",
  "theme_color": "#111827",
  "orientation": "portrait-primary",
  "categories": ["business", "productivity", "utilities"],
  "icons": [
    {
      "src": "/icons/icon.svg",
      "sizes": "any",
      "type": "image/svg+xml",
      "purpose": "any"
    },
    {
      "src": "/icons/maskable-icon.svg",
      "sizes": "any",
      "type": "image/svg+xml",
      "purpose": "maskable"
    }
  ],
  "shortcuts": [
    {
      "name": "Validar ticket",
      "short_name": "Validar",
      "url": "/Success",
      "description": "Abrir la pantalla de escaneo."
    }
  ]
}
```

Campos importantes:

- `name`: nombre completo de la app.
- `short_name`: nombre corto mostrado al instalar.
- `start_url`: ruta que abre la app instalada.
- `scope`: rutas controladas por la PWA.
- `display`: `standalone` hace que parezca una app.
- `theme_color`: color usado por el navegador o sistema.
- `icons`: iconos usados para instalar la app.
- `shortcuts`: accesos rapidos, por ejemplo abrir directamente `/Success`.

## Paso 2: Enlazar el manifest en el layout

Archivo:

```text
Views/Shared/_Layout.cshtml
```

En el `<head>` se agregan los metadatos PWA:

```html
<meta name="theme-color" content="#111827">
<meta name="application-name" content="Success Tickets">
<meta name="apple-mobile-web-app-capable" content="yes">
<meta name="apple-mobile-web-app-title" content="Success Tickets">
<link rel="manifest" href="~/manifest.webmanifest">
<link rel="icon" type="image/svg+xml" href="~/icons/icon.svg">
```

Esto permite que el navegador detecte que la aplicacion es instalable.

## Paso 3: Crear el service worker

Archivo:

```text
wwwroot/service-worker.js
```

El service worker es el archivo JavaScript que corre en segundo plano y controla cache, instalacion y respuesta offline.

En este proyecto se usa:

```javascript
const CACHE_NAME = "success-tickets-pwa-v2";
const STATIC_ASSETS = [
    "/manifest.webmanifest",
    "/offline.html",
    "/favicon.ico",
    "/icons/icon.svg",
    "/icons/maskable-icon.svg",
    "/css/layout.css",
    "/css/site.css",
    "/js/site.js",
    "/lib/html5-qrcode/html5-qrcode.min.js"
];
```

`CACHE_NAME` identifica la version del cache. Cuando cambies assets importantes, cambia el nombre, por ejemplo:

```javascript
const CACHE_NAME = "success-tickets-pwa-v2";
```

`STATIC_ASSETS` contiene los archivos que se guardan en cache al instalar la PWA.

## Paso 4: Instalar assets en cache

Dentro de `service-worker.js`, el evento `install` guarda los archivos principales:

```javascript
self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then((cache) => cache.addAll(STATIC_ASSETS))
            .then(() => self.skipWaiting())
    );
});
```

Esto prepara la app para cargar estilos, scripts, iconos y la pagina offline aunque la red falle.

## Paso 5: Limpiar caches viejos

El evento `activate` elimina versiones antiguas:

```javascript
self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys()
            .then((cacheNames) => Promise.all(
                cacheNames
                    .filter((cacheName) => cacheName !== CACHE_NAME)
                    .map((cacheName) => caches.delete(cacheName))
            ))
            .then(() => self.clients.claim())
    );
});
```

Esto evita que el navegador mantenga archivos viejos despues de publicar una nueva version.

## Paso 6: Manejar navegacion offline

El evento `fetch` decide que hacer con cada request:

```javascript
self.addEventListener("fetch", (event) => {
    const request = event.request;

    if (request.method !== "GET") {
        return;
    }

    const url = new URL(request.url);

    if (url.origin !== self.location.origin) {
        return;
    }

    if (request.mode === "navigate") {
        event.respondWith(
            fetch(request).catch(() => caches.match("/offline.html"))
        );
        return;
    }

    if (isStaticAsset(url.pathname)) {
        event.respondWith(cacheFirst(request));
    }
});
```

Reglas actuales:

- Solo se interceptan requests `GET`.
- No se interceptan llamadas externas.
- Si el usuario navega sin conexion, se muestra `offline.html`.
- Los assets estaticos usan estrategia `cache first`.
- Los `POST`, como validar tickets, no se cachean.

Esto es importante por seguridad: no se deben cachear respuestas sensibles de tickets ni credenciales.

## Paso 7: Crear la pagina offline

Archivo:

```text
wwwroot/offline.html
```

Esta pagina se muestra cuando el navegador intenta abrir una ruta y no tiene conexion:

```html
<h1>Sin conexion</h1>
<p>Success Tickets necesita conexion con el servidor para iniciar sesion y validar tickets. Revisa la red e intenta de nuevo.</p>
```

La pagina offline no reemplaza la validacion. Solo informa que la app necesita red para iniciar sesion y validar tickets.

## Paso 8: Registrar el service worker

Archivo:

```text
Views/Shared/_Layout.cshtml
```

Al final del layout se registra el service worker:

```html
<script>
    if ("serviceWorker" in navigator) {
        window.addEventListener("load", function () {
            navigator.serviceWorker.register("@Url.Content("~/service-worker.js")").catch(function () {
                // La aplicacion sigue funcionando aunque el navegador no instale el worker.
            });
        });
    }
</script>
```

Esto hace que todas las vistas que usan el layout compartido puedan instalar y activar la PWA.

## Paso 9: Leer QR con la camara

La lectura del QR se hace en JavaScript porque la camara del celular pertenece al navegador. C# no puede abrir directamente la camara del usuario desde el servidor.

Archivo de la libreria:

```text
wwwroot/lib/html5-qrcode/html5-qrcode.min.js
```

Archivo donde se carga:

```text
Views/Success/Index.cshtml
```

```html
<script src="~/lib/html5-qrcode/html5-qrcode.min.js" asp-append-version="true"></script>
<script src="~/js/site.js" asp-append-version="true"></script>
```

Flujo implementado:

1. El operador toca `Use camera`.
2. El navegador pide permiso para usar la camara.
3. `html5-qrcode` lee el QR desde la camara trasera.
4. El codigo detectado se copia en `scanInput`.
5. El mismo JavaScript llama a `validateTicket`.
6. `validateTicket` envia el codigo a `POST /Success/ValidateTicket`.
7. C# valida contra la API central y devuelve el resultado.
8. La pantalla muestra permitido o rechazado.

Por eso la mejor division es:

- JavaScript: abrir camara y leer QR.
- C#: validar el codigo con la API central.

## Paso 10: Servir archivos estaticos desde ASP.NET Core

Archivo:

```text
Program.cs
```

El proyecto publica archivos de `wwwroot` con:

```csharp
app.MapStaticAssets();
```

Y la ruta MVC principal con:

```csharp
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Success}/{action=Index}/{id?}")
    .WithStaticAssets();
```

Gracias a esto el navegador puede descargar:

- `/manifest.webmanifest`
- `/service-worker.js`
- `/offline.html`
- `/icons/icon.svg`
- `/css/layout.css`
- `/css/site.css`
- `/js/site.js`
- `/lib/html5-qrcode/html5-qrcode.min.js`

## Como probar la PWA

Ejecuta el proyecto:

```bash
dotnet run
```

Abre la URL local configurada, por ejemplo:

```text
http://localhost:5110
```

En Chrome o Edge:

1. Abre DevTools.
2. Entra a `Application`.
3. Revisa `Manifest`.
4. Revisa `Service Workers`.
5. Verifica que `service-worker.js` este registrado.
6. Activa modo offline desde DevTools.
7. Recarga una ruta de la app.
8. Debe aparecer `offline.html` si no hay conexion.

En produccion debe usarse HTTPS. Los navegadores solo permiten PWA completa en:

- `localhost`;
- dominios con HTTPS.

## Como instalarla desde el navegador

Cuando el navegador detecta manifest valido y service worker registrado:

1. Abre la aplicacion.
2. En Chrome o Edge, busca el icono de instalar en la barra de direcciones.
3. Selecciona `Instalar`.
4. La app se abrira como ventana independiente.

En Android tambien puede aparecer la opcion `Agregar a pantalla principal`.

## Que funciona offline

Funciona offline:

- pagina offline;
- iconos;
- CSS principal;
- JavaScript principal;
- manifest;
- recursos estaticos cacheados.
- libreria local para leer QR con la camara.

No funciona offline:

- iniciar sesion;
- cerrar sesion contra la API central;
- validar tickets;
- consultar datos reales de tickets.

La razon es que esas operaciones dependen de la API central y usan requests `POST` o datos sensibles.

## Como actualizar la PWA

Cuando cambies archivos estaticos importantes:

1. Edita el archivo necesario, por ejemplo `site.css` o `site.js`.
2. Cambia la version de cache en `wwwroot/service-worker.js`.

Ejemplo:

```javascript
const CACHE_NAME = "success-tickets-pwa-v2";
```

3. Publica la aplicacion.
4. Abre la app en el navegador.
5. El service worker nuevo elimina el cache anterior en el evento `activate`.

Si no cambias `CACHE_NAME`, algunos usuarios pueden seguir viendo assets viejos.

## Recomendaciones para este proyecto

- Mantener la validacion de tickets siempre online.
- No cachear respuestas de `/Success/ValidateTicket`.
- No cachear tokens, datos personales ni informacion sensible de tickets.
- Si se agregan nuevos CSS o JS propios, agregarlos a `STATIC_ASSETS`.
- Si se agregan imagenes o iconos importantes, agregarlos a `STATIC_ASSETS`.
- Si se actualiza `html5-qrcode`, cambiar `CACHE_NAME`.
- Usar HTTPS en produccion.
- Probar en DevTools despues de cada cambio de cache.

## Resumen de implementacion

Para implementar la PWA en este proyecto se hicieron estos cambios:

1. Se creo `wwwroot/manifest.webmanifest`.
2. Se crearon los iconos en `wwwroot/icons/`.
3. Se creo `wwwroot/offline.html`.
4. Se creo `wwwroot/service-worker.js`.
5. Se enlazo el manifest en `Views/Shared/_Layout.cshtml`.
6. Se agregaron metadatos PWA al `<head>`.
7. Se registro el service worker al cargar la pagina.
8. Se agrego `html5-qrcode` para leer QR con la camara del celular.
9. Se conecto el QR leido con `POST /Success/ValidateTicket`.
10. Se mantuvo la validacion de tickets online para no cachear datos sensibles.
