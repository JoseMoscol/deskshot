# Descargar y usar DeskShot

## Descarga para Windows x64

Abre [la última versión publicada](https://github.com/JoseMoscol/deskshot/releases/latest). En **Assets**, elige **DeskShot-0.7.0-win-x64-Setup.exe** para instalar, o **DeskShot-0.7.0-win-x64.zip** para usarlo en modo portable. Los archivos automáticos **Source code** contienen el proyecto para desarrolladores, no la aplicación preparada para ejecutar.

Ambas ediciones incluyen el runtime de .NET y los modelos OCR de español e inglés. No necesitas Visual Studio, el SDK ni instalar .NET por separado.

## Instalar con Setup.exe

1. Cierra cualquier versión abierta desde el icono de DeskShot en la bandeja.
2. Abre **DeskShot-0.7.0-win-x64-Setup.exe** y sigue el asistente en español.
3. Elige la carpeta de destino. Por defecto se instala para tu usuario en `%LOCALAPPDATA%\Programs\DeskShot`.
4. El acceso de Inicio se crea automáticamente. Puedes elegir también un acceso en el escritorio.
5. Al terminar puedes abrir DeskShot desde el asistente o desde Inicio.

El instalador incluye el redistribuible **Visual C++ x64 oficial de Microsoft** para el OCR y solo lo ejecuta si faltan bibliotecas compatibles. En ese caso Windows puede pedir permisos de administrador para esa dependencia; la aplicación se instala para tu usuario. No hace falta internet durante la instalación. Si el runtime requiere reiniciar, sigue el aviso del asistente y vuelve a ejecutar Setup cuando corresponda. Si la dependencia falla o no se autoriza, la instalación de DeskShot no continúa como si hubiese terminado correctamente.

El instalador no cierra procesos a la fuerza. A partir de la versión 0.5.1, una instancia abierta bloquea la actualización o desinstalación: sal desde la bandeja y vuelve a intentarlo.

## Alternativa: preparar el ZIP portable

1. Usa Windows 11 de 64 bits (procesadores Intel o AMD). Para Windows 10, consulta las [ediciones compatibles con .NET 10](https://learn.microsoft.com/en-us/dotnet/core/install/windows#supported-versions). Esta descarga no es una compilación para Windows ARM64 ni para sistemas de 32 bits.
2. Descarga el ZIP y selecciona **Extraer todo** en el Explorador. Extrae la carpeta completa en una ubicación permanente de tu usuario, por ejemplo `Documentos\Aplicaciones\DeskShot`.
3. Entra en la carpeta extraída `DeskShot` y abre **DeskShot.exe**. No lo ejecutes dentro del ZIP ni muevas únicamente el `.exe`: necesita los archivos y las carpetas que lo acompañan.
4. Opcionalmente, crea un acceso directo a `DeskShot.exe` desde el Explorador. La aplicación no necesita ejecutarse como administrador.

En el ZIP portable, el motor OCR nativo necesita **Microsoft Visual C++ Redistributable x64** disponible en el equipo. Si la extracción de texto muestra un error de dependencias nativas, instala el [paquete x64 oficial de Microsoft](https://aka.ms/vc14/vc_redist.x64.exe) y vuelve a abrir DeskShot. La información y los requisitos actuales de ese paquete están en la [documentación de Microsoft](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). Una vez disponibles esas bibliotecas, el OCR funciona sin internet, sin cuentas y sin descargar idiomas.

La aplicación todavía no tiene firma de código.

## Primera captura

- **Capturar región**, o **Ctrl + Shift + 2**, abre el modo flotante. Selecciona un área y ajusta sus bordes antes de copiar o guardar.
- Las herramientas permiten dibujar, añadir texto, seleccionar, mover y redimensionar anotaciones.
- **OCR** extrae texto localmente en español e inglés y permite copiarlo al portapapeles.
- **Censurar (C), Pixelar (X), Desenfocar (D):** arrastra una zona para ocultarla; los filtros tienen intensidad ajustable. Para datos sensibles usa un bloque sólido. El OCR no extrae texto de las áreas cubiertas por censura.
- **Pasos (N):** haz clic varias veces para numerar 1, 2, 3…; usa Seleccionar (V) para mover o cambiar el tamaño. El campo Paso permite editar el número.
- **Anclar / Ctrl+I:** conserva una copia encima de otras ventanas, con zoom y transparencia. Arrastra la barra para moverla; Ctrl+0 restablece la vista. Copiar conserva la resolución completa; Editor permite guardar la imagen. Esc cierra la referencia temporal.
- **Capturar con desplazamiento** une un documento hacia abajo. Colócalo al principio y selecciona solo su contenido desplazable, excluyendo encabezados fijos y barras. **Esc** termina y conserva lo capturado.
- En **Configuración** puedes cambiar el atajo, la carpeta de guardado y PNG/JPG. Para conservar todos los detalles usa **PNG**; JPG comprime con pérdida.

El [README](../README.md#uso) explica las herramientas y los límites de captura con más detalle.

## Cerrar y volver a abrir

Por defecto, cerrar la ventana principal deja DeskShot en la bandeja del sistema. Para terminarlo, usa **Salir** en su icono de la bandeja o **Salir de DeskShot** dentro de la aplicación. Si al abrirlo indica que ya está ejecutándose, busca ese icono: solo se permite una instancia.

## Actualizar

**Desde 0.6.0 o anterior:** cierra DeskShot desde la bandeja y ejecuta una vez el instalador de la 0.7.0. No necesitas desinstalar antes. Esa actualización incorpora el nuevo menú.

**Desde la 0.7.0:**

1. Abre **Actualizaciones** en la ventana principal o **Buscar actualizaciones** desde la bandeja.
2. Consulta la versión disponible y sus novedades. Pulsa **Descargar actualización**; puedes cancelar y volver a intentarlo. Se comprueban el tamaño y la huella SHA-256 antes de permitir la instalación.
3. Guarda o copia las referencias ancladas que quieras conservar. Pulsa **Instalar y abrir DeskShot** y resuelve los cambios pendientes de los editores.
4. La app se cierra y abre el asistente. Sigue sus pasos; al terminar se vuelve a abrir DeskShot. Si cancelas el asistente, se intenta volver a abrir la versión anterior.

En **Configuración → Buscar actualizaciones al iniciar** puedes activar los avisos de nuevas versiones; está desactivado por defecto. No descarga ni instala automáticamente. La búsqueda y descarga necesitan internet y solo consultan GitHub; las capturas y el OCR siguen funcionando localmente y nunca se envían.

La configuración se conserva en `%LOCALAPPDATA%\DeskShot\settings.json`. Las capturas guardadas permanecen en la carpeta que elegiste. Los instaladores descargados quedan en `%LOCALAPPDATA%\DeskShot\Updates` y se verifican de nuevo antes de ejecutarse. La comprobación SHA-256 no sustituye una firma de código.

**Edición portable:** el botón **Instalar edición normal** instala la edición con asistente y acceso en Inicio. Para seguir usando el ZIP, extrae manualmente la nueva versión en otra carpeta y actualiza tu acceso directo. Si no hay conexión o falla GitHub, también puedes instalar manualmente desde Releases.

## Quitar la aplicación

Si lo instalaste con Setup, sal de DeskShot y usa **Configuración de Windows → Aplicaciones → DeskShot → Desinstalar**. Se quitan el programa y sus accesos; se conservan tus capturas, tus preferencias y el runtime compartido de Visual C++.

Si usas el ZIP portable, sal de DeskShot y elimina la carpeta que extrajiste y sus accesos directos. Si también quieres restablecer las preferencias, elimina la carpeta `%LOCALAPPDATA%\DeskShot` después de cerrar la aplicación; no elimines tu carpeta de capturas salvo que también quieras borrar esas imágenes.

## Comprobar la descarga

Cada descarga incluye su archivo `.sha256`. Puedes comprobar el instalador desde PowerShell (para el ZIP usa su nombre):

```powershell
Get-FileHash .\DeskShot-0.7.0-win-x64-Setup.exe -Algorithm SHA256
```

El resultado debe coincidir con el valor del archivo publicado. Los archivos de aplicación y los modelos del OCR se incluyen dentro del ZIP; no debes descargar DLLs sueltas de otros sitios.
