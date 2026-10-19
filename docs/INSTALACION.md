# Descargar y usar DeskShot

## Descarga para Windows x64

Abre [la última versión publicada](https://github.com/JoseMoscol/deskshot/releases/latest) y descarga **DeskShot-0.5.0-win-x64.zip** en la sección **Assets**. Los archivos automáticos **Source code** contienen el proyecto para desarrolladores, no la aplicación preparada para ejecutar.

Esta edición incluye el runtime de .NET y los modelos OCR de español e inglés. No necesitas Visual Studio, el SDK ni instalar .NET por separado. Es una distribución **portable en ZIP**, sin asistente `Setup.exe`.

## Preparar la aplicación

1. Usa Windows 11 de 64 bits (procesadores Intel o AMD). Para Windows 10, consulta las [ediciones compatibles con .NET 10](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions). Esta descarga no es una compilación para Windows ARM64 ni para sistemas de 32 bits.
2. Descarga el ZIP y selecciona **Extraer todo** en el Explorador. Extrae la carpeta completa en una ubicación permanente de tu usuario, por ejemplo `Documentos\Aplicaciones\DeskShot`.
3. Entra en la carpeta extraída `DeskShot` y abre **DeskShot.exe**. No lo ejecutes dentro del ZIP ni muevas únicamente el `.exe`: necesita los archivos y las carpetas que lo acompañan.
4. Opcionalmente, crea un acceso directo a `DeskShot.exe` desde el Explorador. La aplicación no necesita ejecutarse como administrador.

El motor OCR nativo necesita **Microsoft Visual C++ Redistributable x64**. Muchos equipos ya lo tienen instalado. Si la extracción de texto muestra un error de dependencias nativas, instala el [paquete x64 oficial de Microsoft](https://aka.ms/vc14/vc_redist.x64.exe) y vuelve a abrir DeskShot. La información y los requisitos actuales de ese paquete están en la [documentación de Microsoft](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). Una vez disponibles esas bibliotecas, el OCR funciona sin internet, sin cuentas y sin descargar idiomas.

La aplicación todavía no tiene firma de código.

## Primera captura

- **Capturar región**, o **Ctrl + Shift + 2**, abre el modo flotante. Selecciona un área y ajusta sus bordes antes de copiar o guardar.
- Las herramientas permiten dibujar, añadir texto, seleccionar, mover y redimensionar anotaciones.
- **OCR** extrae texto localmente en español e inglés y permite copiarlo al portapapeles.
- **Capturar con desplazamiento** une un documento hacia abajo. Colócalo al principio y selecciona solo su contenido desplazable, excluyendo encabezados fijos y barras. **Esc** termina y conserva lo capturado.
- En **Configuración** puedes cambiar el atajo, la carpeta de guardado y PNG/JPG. Para conservar todos los detalles usa **PNG**; JPG comprime con pérdida.

El [README](../README.md#uso) explica las herramientas y los límites de captura con más detalle.

## Cerrar y volver a abrir

Por defecto, cerrar la ventana principal deja DeskShot en la bandeja del sistema. Para terminarlo, usa **Salir** en su icono de la bandeja o **Salir de DeskShot** dentro de la aplicación. Si al abrirlo indica que ya está ejecutándose, busca ese icono: solo se permite una instancia.

## Actualizar

1. Sal de DeskShot desde la bandeja.
2. Descarga y extrae el nuevo ZIP en otra carpeta.
3. Abre el nuevo `DeskShot.exe` y actualiza tu acceso directo si corresponde.

La configuración se conserva en `%LOCALAPPDATA%\DeskShot\settings.json`. Las capturas guardadas permanecen en la carpeta que elegiste. No hay un actualizador automático.

## Quitar la aplicación

Sal de DeskShot y elimina la carpeta que extrajiste y sus accesos directos. Tus capturas y tu configuración se conservan por separado. Si también quieres restablecer las preferencias, elimina la carpeta `%LOCALAPPDATA%\DeskShot` después de cerrar la aplicación; no elimines tu carpeta de capturas salvo que también quieras borrar esas imágenes.

## Comprobar la descarga

Cada Release incluye un archivo `.sha256` con la huella del ZIP. Puedes comprobarla desde PowerShell:

```powershell
Get-FileHash .\DeskShot-0.5.0-win-x64.zip -Algorithm SHA256
```

El resultado debe coincidir con el valor del archivo publicado. Los archivos de aplicación y los modelos del OCR se incluyen dentro del ZIP; no debes descargar DLLs sueltas de otros sitios.
