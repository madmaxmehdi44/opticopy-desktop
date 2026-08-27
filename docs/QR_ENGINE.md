# QR Engine

`OptiCopy.Imaging` owns QR encoding/decoding and stays independent from WinUI.

The implementation uses ZXing.Net 0.16.11. QR generation produces an immutable `QrMatrix`; `QrMatrixRasterizer` can convert it to Gray8 pixels for display or test pipelines. QR decoding accepts Gray8, RGB24, BGRA32, and RGBA32 buffers and restricts detection to QR codes.

The WinUI camera integration should feed preview frames into `QrCodeDecoder` on a background pipeline. The decoder must not be called from the UI thread for continuous scanning.

The QR engine is intentionally separate from the Decimen/DOT1 protocol. A protocol packet is generated first; the resulting packet string is then encoded as a QR payload. On receive, QR text is decoded first and then passed to `OptiCopy.Core` for protocol parsing and fountain reconstruction.

Reference implementation: `bashalarmistalt/decimen-optical-transfer`.
