// Generate the QR code locally; no TOTP secret is sent to a QR image service.
const target = document.getElementById('qrcode');
if (target && typeof qrcode === 'function') {
    const qr = qrcode(0, 'M');
    qr.addData(target.dataset.otpauthUri);
    qr.make();
    target.innerHTML = qr.createSvgTag({ cellSize: 5, margin: 8, alt: 'Authenticator app setup QR code' });
}
