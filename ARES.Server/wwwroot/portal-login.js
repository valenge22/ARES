// Evita depender de variables globales creadas por los id del HTML.
window.submitLogin = async function (event) {
  event.preventDefault();
  const message = document.getElementById('loginError');
  const email = document.getElementById('email');
  const password = document.getElementById('password');
  message.textContent = 'Ingresando…';
  try {
    const response = await fetch('/api/auth/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-ARES-Web': '1' },
      body: JSON.stringify({ email: email.value.trim(), password: password.value })
    });
    const raw = await response.text(); let result = {};
    try { result = raw ? JSON.parse(raw) : {}; } catch { throw new Error('El servidor devolvió una respuesta inválida al iniciar sesión.'); }
    if (!response.ok) throw new Error(result.error || 'No se pudo iniciar sesión.');
    if (result.mfaRequired) {
      const code = prompt('Ingresá el código de 6 dígitos o un código de recuperación ARES');
      if (!code) throw new Error('Falta completar la verificación en dos pasos.');
      const recovery = code.trim().toUpperCase().startsWith('ARES-');
      const verification = await fetch(recovery ? '/api/auth/mfa/recover' : '/api/auth/mfa/verify', {
        method: 'POST', headers: { 'Content-Type': 'application/json', 'X-ARES-Web': '1' },
        body: JSON.stringify(recovery ? { accessToken: result.accessToken, refreshToken: result.refreshToken, factorId: result.factorId, recoveryCode: code } : { accessToken: result.accessToken, factorId: result.factorId, code })
      });
      const verifiedRaw = await verification.text();
      try { result = verifiedRaw ? JSON.parse(verifiedRaw) : {}; } catch { throw new Error('El servidor devolvió una respuesta inválida al verificar 2FA.'); }
      if (!verification.ok) throw new Error(result.error || 'Código incorrecto.');
    }
    auth = result;
    await start();
  } catch (error) { message.textContent = error?.message || 'No se pudo iniciar sesión.'; }
};
