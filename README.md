# Jellyfin TMDb Auto Import (MVP)

Plugin de Jellyfin que implementa un flujo de:

1. Buscar una pelicula o serie en tu biblioteca.
2. Si no existe, buscar en TMDb.
3. Importar un item placeholder en una carpeta de importacion (STRM/NFO para peliculas, NFO para series).

## Estado

MVP funcional a nivel de plugin backend/API.

- No intercepta automaticamente la barra de busqueda nativa del cliente web de Jellyfin.
- Expone un endpoint que puedes integrar con tu flujo (boton personalizado, script, app externa).

## Endpoint

`GET /Plugins/TmdbAutoImport/search-or-import?query=...&type=movie|series`

Respuesta:

- Si encuentra en biblioteca: devuelve `source=library` y `items`.
- Si no encuentra y TMDb tiene resultados: importa el primer resultado y devuelve `source=tmdb`.

## Configuracion del plugin

Configura estos campos en `PluginConfiguration`:

- `TmdbApiKey`: API key de TMDb.
- `MoviesImportPath`: ruta donde se importan peliculas.
- `SeriesImportPath`: ruta donde se importan series.
- `ImportRootPath`: ruta legacy opcional (fallback) si no defines las dos rutas separadas.
- `Language`: idioma para TMDb (ej: `es-ES`).
- `Country`: region para TMDb (ej: `ES`).
- `MovieStrmUrlTemplate`: URL plantilla del `.strm`, usa `{tmdbId}`.

## Build

```bash
dotnet build Jellyfin.Plugin.TmdbAutoImport/Jellyfin.Plugin.TmdbAutoImport.csproj
```

## Instalacion en Jellyfin

1. Compila el proyecto.
2. Copia el DLL generado a la carpeta de plugins de Jellyfin en un subdirectorio propio.
3. Reinicia Jellyfin.
4. Configura tu API key y ruta de importacion.
5. Ejecuta una exploracion de biblioteca para que indexe los placeholders creados.

## Instalar desde repositorio en Jellyfin

Este repo incluye un `manifest.json` compatible con el catalogo de Jellyfin.

URL del repositorio para pegar en Jellyfin:

`https://raw.githubusercontent.com/JonaxHS/tmdbsearchJellyfin/main/manifest.json`

Pasos:

1. Dashboard -> Catalogo de plugins.
2. Repositorios -> Agregar.
3. Pega la URL anterior.
4. Guarda y recarga el catalogo.
5. Instala **TMDb Auto Import** desde la categoria General.
