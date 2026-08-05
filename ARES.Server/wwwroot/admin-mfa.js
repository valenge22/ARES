showSecurity=async function(){
  $('securityCard').classList.remove('hidden');
  try{
    const factors=await api('/api/account/mfa');
    const active=factors.find(x=>x.factor_type==='totp'&&x.status==='verified');
    mfaFactorId=active?.id||'';
    $('mfaStatus').textContent=active?'2FA activo. Se solicitará un código al iniciar sesión.':'2FA desactivado.';
    $('mfaEnable').classList.toggle('hidden',!!active);
    $('mfaDisable').classList.toggle('hidden',!active);
    let recoveryButton=$('mfaRecovery');if(active&&!recoveryButton){recoveryButton=document.createElement('button');recoveryButton.id='mfaRecovery';recoveryButton.className='secondary';recoveryButton.style.marginLeft='8px';recoveryButton.textContent='Generar códigos de recuperación';recoveryButton.onclick=generateRecoveryCodes;$('mfaDisable').after(recoveryButton)}else if(recoveryButton)recoveryButton.classList.toggle('hidden',!active);
  }catch(x){$('mfaStatus').textContent=`No se pudo consultar 2FA: ${x.message}`}
  try{const s=await api('/api/account/sessions'),rows=Array.isArray(s)?s:(s.$values||s.sessions||[]);$('sessions').innerHTML=rows.length?rows.map(x=>`<tr><td>${esc(x.clientName)}</td><td>${esc(x.ipAddress)}</td><td>${new Date(x.lastSeenAt).toLocaleString('es-AR')}</td><td>${x.revoked?'Cerrada':'Activa'}</td><td>${x.revoked?'—':`<button class="danger" onclick="closeSession('${x.sessionId}')">Cerrar</button>`}</td></tr>`).join(''):'<tr><td colspan="5">No hay sesiones registradas.</td></tr>'}catch(x){$('sessions').innerHTML=`<tr><td colspan="5">${esc(x.message)}</td></tr>`}
  try{const e=await api('/api/account/login-events'),rows=Array.isArray(e)?e:(e.$values||e.events||[]);$('events').innerHTML=rows.length?rows.map(x=>`<tr><td>${esc(x.clientName)}</td><td>${esc(x.ipAddress)}</td><td>${x.successful?'Correcto':'Fallido'}</td><td>${new Date(x.occurredAt).toLocaleString('es-AR')}</td></tr>`).join(''):'<tr><td colspan="4">No hay accesos registrados.</td></tr>'}catch(x){$('events').innerHTML=`<tr><td colspan="4">${esc(x.message)}</td></tr>`}
};

beginMfa=async function(){
  $('securityMessage').textContent='Preparando 2FA…';$('mfaEnable').disabled=true;
  try{const x=await api('/api/account/mfa/enroll',{method:'POST'});mfaFactorId=x.id;$('mfaQr').src=x.totp.svg?URL.createObjectURL(new Blob([x.totp.svg],{type:'image/svg+xml'})):x.totp.qr_code;$('mfaSecret').textContent=`Clave manual: ${x.totp.secret}`;$('mfaSetup').classList.remove('hidden');$('securityMessage').textContent='Escaneá el QR, ingresá el código y confirmá.'}
  catch(x){$('securityMessage').textContent=`No se pudo activar 2FA: ${x.message}`;alert($('securityMessage').textContent)}finally{$('mfaEnable').disabled=false}
};

confirmMfa=async function(){
  const code=$('mfaCode').value.replace(/\D/g,'');if(code.length!==6){$('securityMessage').textContent='Ingresá los 6 dígitos del autenticador.';return}
  try{const x=await api('/api/account/mfa/verify',{method:'POST',body:JSON.stringify({accessToken:'',factorId:mfaFactorId,code})});auth=x;localStorage.setItem('ares.platform.auth',JSON.stringify(auth));$('mfaSetup').classList.add('hidden');$('securityMessage').textContent='2FA activado correctamente.';if(x.recoveryCodes)showRecoveryCodes(x.recoveryCodes);await showSecurity()}
  catch(x){$('securityMessage').textContent=`No se pudo confirmar 2FA: ${x.message}`;alert($('securityMessage').textContent)}
};

function showRecoveryCodes(codes){
  const text=['CÓDIGOS DE RECUPERACIÓN ARES','Guardalos en un lugar seguro. Cada código funciona una sola vez.','',...codes].join('\n');
  const overlay=document.createElement('div');overlay.style='position:fixed;inset:0;background:#0f172acc;z-index:9999;display:grid;place-items:center;padding:20px';
  overlay.innerHTML=`<div style="background:white;border-radius:14px;padding:26px;width:min(520px,95vw)"><h2>Códigos de recuperación</h2><p>Se mostrarán solamente ahora. Guardalos antes de cerrar.</p><textarea readonly style="width:100%;height:260px;padding:12px">${text}</textarea><div style="display:flex;gap:8px;justify-content:flex-end;margin-top:12px"><button id="recoveryDownload" class="primary">Descargar TXT</button><button id="recoveryClose">Ya los guardé</button></div></div>`;
  document.body.appendChild(overlay);overlay.querySelector('#recoveryDownload').onclick=()=>{const a=document.createElement('a');a.href=URL.createObjectURL(new Blob([text],{type:'text/plain'}));a.download='ARES-Codigos-Recuperacion.txt';a.click()};overlay.querySelector('#recoveryClose').onclick=()=>overlay.remove();
}

async function generateRecoveryCodes(){
  if(!confirm('Los códigos anteriores dejarán de funcionar. ¿Generar códigos nuevos?'))return;
  try{const x=await api('/api/account/mfa/recovery-codes',{method:'POST'});showRecoveryCodes(x.recoveryCodes)}catch(x){alert(`No se pudieron generar los códigos: ${x.message}`)}
}

signIn=async function(e){
  e.preventDefault();$('error').textContent='Ingresando…';
  try{const r=await fetch('/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email:$('email').value,password:$('password').value})});let x=await r.json();if(!r.ok)throw new Error(x.error);if(x.mfaRequired){const code=prompt('Ingresá el código de 6 dígitos o un código de recuperación ARES');if(!code)throw new Error('Falta completar 2FA.');const recovery=code.trim().toUpperCase().startsWith('ARES-');const vr=await fetch(recovery?'/api/auth/mfa/recover':'/api/auth/mfa/verify',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(recovery?{accessToken:x.accessToken,refreshToken:x.refreshToken,factorId:x.factorId,recoveryCode:code}:{accessToken:x.accessToken,factorId:x.factorId,code})});x=await vr.json();if(!vr.ok)throw new Error(x.error)}auth=x;localStorage.setItem('ares.platform.auth',JSON.stringify(auth));await start()}catch(x){$('error').textContent=x.message}
};
