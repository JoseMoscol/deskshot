# DeskShot

Aplicación de escritorio para **Windows**, escrita en **C# / .NET 10 / WPF**. Captura a resolución nativa, anota, extrae texto con OCR local y exporta sin reducir las dimensiones de la imagen. La aplicación publicada no utiliza servicios de red. Tesseract y sus modelos se incluyen en la distribución; NuGet solo se necesita al restaurar dependencias para compilar.

## Iniciar

Abre `artifacts/DeskShot-0.5/DeskShot.exe`, o haz doble clic en `Iniciar DeskShot.cmd` desde la raíz del proyecto. El lanzador compila si todavía no existe el ejecutable publicado. Si la versión anterior sigue en la bandeja, elige **Salir** en su menú antes de abrir esta actualización. La publicación separada conserva intactos los archivos de la instancia anterior y ambas versiones comparten la configuración existente.

- Para ejecutar: **.NET 10 Desktop Runtime** y una sesión interactiva de Windows 10/11.
- El motor OCR nativo requiere **Microsoft Visual C++ Redistributable 2015–2022** de la arquitectura del proceso (x64 en este equipo). Sus bibliotecas ya estaban instaladas y se verificó el funcionamiento real del motor.
- Para compilar: **SDK .NET 10** con las referencias de escritorio de Windows. El SDK 10.0.402 ya estaba instalado en el equipo de desarrollo.
- La carpeta publicada es dependiente del runtime: hay que conservar todos sus archivos junto al `.exe`. Todavía no incluye instalador, firma de código ni actualizador.

## Uso

1. Pulsa **Capturar región** o **Ctrl + Shift + 2**. El escritorio se congela y oscurece; arrastra y suelta para elegir el área. La selección permanece abierta con sus barras flotantes.
2. **Ajustar área (B):** arrastra el fondo para mover el encuadre. Sus ocho tiradores y todos sus bordes permiten ajustar el recorte antes de copiar o guardar. En este modo también puedes arrastrar fuera de la región para elegir otra. **Capturar escritorio completo** inicia el modo flotante con todo el escritorio seleccionado.
3. La barra lateral ofrece **seleccionar (V), lápiz (P), flecha (A), rectángulo (R), resaltador (H) y texto (T)**. El cuadro de color abre la paleta y el grosor. Las anotaciones mantienen su edición, selección, movimiento y tamaño.
4. **Copiar / Ctrl+C / Enter** copia la imagen compuesta y termina la captura. **Guardar / Ctrl+S** crea un archivo en la carpeta configurada y termina. **Ctrl+Shift+S** permite elegir nombre, ubicación y PNG/JPG.
5. **OCR / Ctrl+T** extrae texto de la región original con el motor local de español e inglés. Puedes revisar y corregirlo, copiar todo con **Copiar texto / Ctrl+Shift+C**, o seleccionar una parte y pulsar **Ctrl+C**. Cerrar el OCR devuelve a la captura flotante. No se sobrescribe automáticamente el texto copiado con una imagen.
6. **Editor / Ctrl+E** continúa en el editor completo, conservando el recorte y las anotaciones. Allí **Ajustar / Ctrl+0** y el zoom cambian solo la vista previa. El editor completo mantiene sus propios botones Copiar, Guardar y Guardar como.
7. **Ctrl+Z / Ctrl+Y** deshace y rehace anotaciones y cambios del área. **Esc** cancela el arrastre en curso; fuera de un arrastre cancela la captura. El botón × de la barra inferior también cancela.

La región se guarda en píxeles del escritorio original. Cambiar sus bordes o posición ajusta el encuadre, sin deformar ni trasladar las anotaciones; lo que quede fuera se recorta al exportar y reaparece si amplías el área. El tamaño mostrado es el ancho y alto exactos del archivo final. Las barras, la máscara oscura y los tiradores nunca forman parte de la imagen guardada o copiada.

Las barras se colocan junto a la región y cambian de lado o entran en el área si no hay espacio fuera. Se mantienen dentro del monitor con mayor parte de la selección, para no perderse entre pantallas. En pantallas pequeñas, la barra lateral permite desplazamiento.

En **Configuración** puedes elegir:

- Atajo global: haz clic en el campo, pulsa la combinación y guarda. Si está ocupada, se conserva el registro anterior.
- Carpeta de destino y formato predeterminado: **PNG sin pérdida** o **JPG con calidad 1–100** (95 inicialmente).
- Espera de 0 a 10 segundos antes de capturar.
- Copia y guardado automáticos: al pulsar **Copiar** se guarda también un archivo si el guardado automático está activado. Al pulsar **Guardar** se copia también la imagen si la copia automática está activada. Al abrir el editor completo se aplican ambos automatismos a la imagen compuesta del recorte.
- Continuar en la bandeja del sistema al cerrar la ventana principal. El menú de la bandeja permite capturar, abrir la aplicación o salir.

La copia automática está activada inicialmente; el guardado automático está desactivado. Elegir una región en modo flotante no copia ni guarda por sí solo: puedes ajustar y anotar antes de terminar. Si editas después en el editor completo, vuelve a copiar o guardar para conservar esa versión. Al cerrar un editor completo con cambios pendientes aparece una confirmación.

## Captura con desplazamiento

1. Abre la página o documento, termina de cargarlo y colócalo al principio. Se captura **desde la posición actual hacia abajo**, sin cambiar el zoom de la aplicación de origen.
2. En Inicio o en la bandeja, pulsa **Capturar con desplazamiento**. Selecciona el interior de la zona que se desplaza, excluyendo encabezados fijos, barras del navegador, barras de desplazamiento y bordes de la ventana. Ajusta sus bordes y pulsa **Desplazar**. Este botón también está disponible desde una captura de región normal.
3. Elige la espera entre avances: 0,6 / 1,5 / 3 segundos. Usa una espera mayor para documentos o páginas que cargan lentamente. Pulsa **Iniciar captura** y deja el ratón y el teclado libres durante el recorrido.
4. DeskShot desplaza la ventana real, espera a que se estabilice y une los fotogramas por su solapamiento. **Esc** o **Terminar y abrir editor** conserva todo lo unido hasta ese momento. Los controles se retiran antes de cada fotograma y nunca se incluyen en la imagen.
5. Al dejar de detectar movimiento, abre el resultado en el editor. Revisa que llegue hasta el final que esperabas. Puedes anotar, usar el **OCR local**, copiar o guardar con los mismos controles y preferencias del editor. Para conservar los píxeles originales elige **PNG**; JPG aplica compresión con pérdida. Usa zoom 100 % para inspeccionar el detalle de una imagen larga.

La unión añade filas originales sin interpolación, escalado ni mezcla de imágenes. Solo admite desplazamiento vertical, conservando el ancho del área seleccionada. Las anotaciones se añaden **después** del recorrido en el editor; si ya anotaste en el modo flotante, deshazlas antes de usar Desplazar.

La captura se detiene ante una unión ambigua, un salto excesivo, una ventana que tapa el documento o un cambio de ventana activa. Conserva el resultado parcial y muestra el motivo en el editor. Un área completamente blanca o repetida puede parecer inmóvil: no se garantiza que esa detección corresponda al final real del documento. Encabezados fijos dentro del área, animaciones, cambios de diseño, desplazamiento horizontal, documentos protegidos y aplicaciones elevadas pueden impedir la captura. La rueda se envía con [SendInput de Windows](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput); Windows limita la inyección en aplicaciones con mayor nivel de privilegios. Una configuración de rueda que avance una pantalla completa puede no dejar suficiente solapamiento: utiliza desplazamiento por líneas en ese caso.

Se limita cada imagen a **32 megapíxeles, 60 000 px de alto y 300 avances** para contener el uso de memoria y detener páginas infinitas. Al alcanzar el límite se abre la parte capturada, sin reducirla. Captura documentos mayores por secciones. La ventana de origen queda en la última posición alcanzada. Estas limitaciones se comunican en el resultado; no se publica una imagen parcial como si fuese un documento completo.

El OCR procesa imágenes de más de 8 000 px de alto por bloques, buscando una fila uniforme cerca de cada corte para reducir divisiones de texto. Mantiene las regiones de palabras en las coordenadas de la captura completa. La lectura sigue siendo local y no modifica la imagen; un corte sin espacio entre líneas puede requerir corregir el texto extraído.

## Texto y selección de anotaciones

- **Texto (T):** haz clic en la imagen, escribe una o varias líneas, elige tamaño en píxeles y negrita, y pulsa Aplicar texto (**Ctrl+Enter**). Después queda seleccionado para moverlo. El diálogo comprueba que el texto quepa en la captura.
- **Seleccionar (V):** haz clic sobre un objeto y arrástralo. En las flechas puedes arrastrar sus dos extremos por separado; los rectángulos, resaltadores, trazos y textos tienen tiradores en las esquinas. El texto mantiene sus proporciones y escala su fuente al redimensionarse.
- **Editar texto:** doble clic sobre el texto, **F2**, o el botón Editar texto. El cambio conserva el mismo objeto y su historial.
- **Color y grosor:** con un objeto seleccionado, los controles de la barra modifican ese objeto. El tamaño de texto se ajusta desde Editar texto o sus esquinas; el control de grosor se utiliza para los dibujos.
- **Mover con precisión:** con el lienzo enfocado, las flechas del teclado mueven la selección un píxel; **Shift + flecha** la mueve diez píxeles.
- **Eliminar / Supr:** elimina la anotación seleccionada. En el editor completo, **Esc** cancela un arrastre en curso o quita la selección; en el modo flotante cancela la captura cuando no hay un arrastre activo. Un clic en una zona vacía con la herramienta Seleccionar también quita la selección.
- Un dibujo nuevo queda seleccionado automáticamente. Atajos de herramientas: **V** seleccionar, **P** lápiz, **A** flecha, **R** rectángulo, **H** resaltador y **T** texto. **Ctrl+T** mantiene el OCR.

Cada arrastre se confirma como una sola acción de deshacer/rehacer. Mover, redimensionar, cambiar propiedades, editar texto y eliminar también se pueden deshacer. La selección y sus tiradores solo aparecen en pantalla; PNG/JPG y el portapapeles de imagen reciben las anotaciones confirmadas a la resolución original.

## Calidad de imagen

- Se capturan **píxeles físicos**, con conciencia de DPI `PerMonitorV2` y soporte de coordenadas negativas del escritorio virtual.
- PNG conserva los valores de píxel del original. JPG mantiene el ancho y alto, pero introduce compresión con pérdida; PNG es preferible para texto pequeño.
- Las anotaciones se registran en coordenadas de la imagen y se componen al tamaño original al exportar.
- La captura usa GDI / `BitBlt` en SDR de 8 bits por canal. No ofrece captura HDR de 10 bits, cursor, ventanas ocultas, contenido protegido ni grabación de vídeo. En HDR puede haber diferencias de color.
- Se ocultan las ventanas de DeskShot antes de capturar. La selección se hace sobre una instantánea congelada del escritorio.
- Se verificó una captura real de 2880 × 1800 y exportaciones sintéticas 4K de 3840 × 2160. La selección física entre monitores con escalas diferentes todavía requiere una prueba manual con ese hardware.

## Estructura y ampliaciones

```text
src/DeskShot/
  Domain/           Documento, anotaciones, historial, coordenadas y ajustes
  Application/      Contratos de captura, exportación, configuración y OCR
  Infrastructure/   GDI, registro de atajos, codificadores y JSON
  Presentation/     Inicio, configuración, selector y editor WPF
tests/DeskShot.Tests/  Pruebas ejecutables y previsualizaciones de la interfaz
```

`CaptureDocument` conserva una imagen original inmutable, una región de recorte y anotaciones con identificadores estables. El historial guarda estados del recorte y las anotaciones, sin duplicar la imagen original. Restaurar una revisión guardada también restaura su identidad para que no se marque como una edición pendiente. `AnnotationEditor` controla selección, vista previa y confirmación de gestos en píxeles; `AnnotationGeometry` y `RegionGeometry` se ocupan de los tiradores y transformaciones. El lienzo y la exportación comparten el dibujo de anotaciones y texto. `CreateRegionDocument` traslada las coordenadas al recorte para continuar en el editor completo sin cambiar la imagen resultante.

**OCR local implementado:** `TesseractOcrEngine` implementa `IOcrEngine.RecognizeAsync`, procesa en segundo plano y devuelve texto y regiones en píxeles mediante `OcrResult`. Usa Tesseract 5.2.0 con los modelos LSTM `spa` y `eng` de `tessdata_fast`, incluidos en `tessdata/`. Las capturas pequeñas se amplían internamente para facilitar la lectura; los resultados vuelven a las coordenadas originales y la captura no se modifica. El OCR usa la imagen original, sin flechas ni trazos añadidos en el editor.

La extracción se realiza en memoria, sin guardar una copia temporal de la captura, sin enviar imágenes y sin descargar modelos al ejecutar. No requiere cuenta ni clave de API. Se conservan los saltos de línea; la exactitud depende del tamaño y contraste del texto. El texto impreso nítido es el caso principal; tablas complejas, texto inclinado y escritura a mano pueden requerir correcciones.

El botón Cancelar marca el trabajo como cancelado y descarta su resultado. La llamada nativa de Tesseract no se interrumpe a mitad de una fase: puede tardar en finalizar la cancelación, mientras la interfaz sigue respondiendo. Cerrar el OCR también cancela y evita publicar resultados tardíos. Las extracciones se serializan para limitar el uso de recursos.

Conserva **`Tesseract.dll`, `x64/`, `x86/`, `tessdata/` y `ThirdParty/`** junto al ejecutable. La procedencia, revisión, hashes y licencias están en `ThirdParty/NOTICE.txt`. Los archivos de idiomas ya están dentro del proyecto; no necesitas una instalación separada de Tesseract. La primera restauración de paquetes al compilar puede requerir internet; las siguientes pueden utilizar la caché `.dotnet/packages`.

La configuración se guarda al pulsar Guardar configuración en `%LOCALAPPDATA%/DeskShot/settings.json`. Los archivos de imagen reciben un nombre con fecha, hora y sufijo único. Tanto el JSON como las imágenes se escriben a un temporal del mismo directorio y después se renombran, para no reemplazar un archivo existente por uno incompleto.

Las anotaciones editables solo se conservan en memoria mientras el editor está abierto. PNG/JPG exportan la imagen compuesta; un formato de proyecto editable y un historial persistente pueden añadirse después.

## Compilar y verificar

Desde PowerShell:

```powershell
.\build.ps1
# Añade una captura real del escritorio (no la guarda):
.\build.ps1 -CheckDesktop
```

El script compila, ejecuta las pruebas y publica en `artifacts/DeskShot-0.5`. Las pruebas verifican selección, historial, independencia del original, PNG 4K píxel a píxel, dimensiones y firmas JPG, anotaciones, configuración, atajos y render de la interfaz. También comprueban selección por orden de capas, tiradores solapados, movimiento y redimensionado con límites, un deshacer por gesto, cancelación, edición de texto, eliminación y controles de propiedades. El modo flotante añade pruebas de los ocho tiradores, ajuste de bordes, recorte exacto, historial de región, posición de las barras y continuidad con el editor. Ejecutan OCR real tanto de imágenes completas como de regiones anotadas, usando el original. Las previsualizaciones y resultados se generan en `artifacts/verification`.

Comandos individuales:

```powershell
dotnet build DeskShot.slnx -c Release -m:1 -nr:false
dotnet tests/DeskShot.Tests/bin/Release/net10.0-windows/DeskShot.Tests.dll artifacts/verification
dotnet publish src/DeskShot/DeskShot.csproj -c Release --self-contained false -o artifacts/DeskShot-0.5 -m:1 -nr:false
```

El registro real de atajos requiere una sesión interactiva de Windows con permiso para `RegisterHotKey`; un entorno aislado puede bloquearlo. La prueba libera sus atajos al terminar. Las previsualizaciones usan contenido sintético, no capturas del usuario.

Las pruebas de desplazamiento comprueban las uniones píxel a píxel, el último avance de un píxel, patrones ambiguos, encabezados fijos, saltos sin solapamiento, límites, cancelación y conservación del resultado parcial. También exportan y anotan una imagen de 22 000 px y ejecutan OCR real en otra de 34 000 px, con texto al inicio, en un bloque intermedio y al final.

Se verificó además un recorrido real en Windows al 150 % de escala: **65 partes unidas en un PNG de 1049 × 5436 px**, con sus 24 secciones reconocidas por OCR local y sin controles de captura incluidos. Este caso usa un documento WPF sintético; no supone compatibilidad comprobada con todas las aplicaciones, navegadores o lectores PDF.

Para una prueba interactiva reproducible con un documento sintético propio, abre dos procesos del ejecutable de pruebas: primero `artifacts/verification --scroll-document` y después `artifacts/verification --scroll-demo`. El primero genera el área física a usar en el segundo. Pulsa Iniciar captura en el panel; el resultado se guarda en `artifacts/verification/scroll-demo-result.png`, sin tocar el portapapeles. Cierra el documento de prueba al terminar. `--verify-scroll-demo` comprueba el PNG resultante con OCR local, buscando sus 24 secciones y la ausencia de los controles de captura.

En un entorno restringido que bloquee los atajos puedes ejecutar las otras pruebas (incluido OCR) con `--skip-hotkeys`.

Para probar manualmente el modo flotante con una imagen sintética, sin capturar el escritorio ni iniciar la bandeja:

```powershell
dotnet tests/DeskShot.Tests/bin/Release/net10.0-windows/DeskShot.Tests.dll artifacts/verification --floating-demo
```

La opción Editor en esa prueba exporta el resultado a `artifacts/verification/floating-demo-result.png`. El proyecto de pruebas usa el mismo manifiesto PerMonitorV2 que DeskShot. Se verificaron interactivamente el ajuste de un borde, movimiento del área, deshacer, dibujo, texto y OCR con el escalado real de Windows. La selección física entre monitores con escalas diferentes aún requiere prueba manual en ese hardware.
