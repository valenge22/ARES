showSecurity=async function(){
  $('securityCard').classList.remove('hidden');
  try{
    const factors=await api('/api/account/mfa');
    const active=factors.find(x=>x.factor_type==='totp'&&x.status==='verified');
    mfaFactorId=active?.id||'';
    $('mfaStatus').textContent=active?'2FA activo. Se solicitará un código al iniciar sesión.':'2FA desactivado.';
    $('mfaEnable').classList.toggle('hidden',!!active);
    $('mfaDisable').classList.toggle('hidden',!active);
  }catch(x){$('mfaStatus').textContent=`No se pudo consultar 2FA: ${x.message}`}
  try{const s=await api('/api/account/sessions');$('sessions').innerHTML=s.map(x=>`<tr><td>${esc(x.clientName)}</td><td>${esc(x.ipAddress)}</td><td>${new Date(x.lastSeenAt).toLocaleString('es-AR')}</td><td>${x.revoked?'Cerrada':'Activa'}</td><td>${x.revoked?'—':`<button class="danger" onclick="closeSession('${x.sessionId}')">Cerrar</button>`}</td></tr>`).join('')}catch(x){$('sessions').innerHTML=`<tr><td colspan="5">${esc(x.message)}</td></tr>`}
  try{const e=await api('/api/account/login-events');$('events').innerHTML=e.map(x=>`<tr><td>${esc(x.clientName)}</td><td>${esc(x.ipAddress)}</td><td>${x.successful?'Correcto':'Fallido'}</td><td>${new Date(x.occurredAt).toLocaleString('es-AR')}</td></tr>`).join('')}catch(x){$('events').innerHTML=`<tr><td colspan="4">${esc(x.message)}</td></tr>`}
};

beginMfa=async function(){
  $('securityMessage').textContent='Preparando 2FA…';$('mfaEnable').disabled=true;
  try{const x=await api('/api/account/mfa/enroll',{method:'POST'});mfaFactorId=x.id;$('mfaQr').src=x.totp.svg?URL.createObjectURL(new Blob([x.totp.svg],{type:'image/svg+xml'})):x.totp.qr_code;$('mfaSecret').textContent=`Clave manual: ${x.totp.secret}`;$('mfaSetup').classList.remove('hidden');$('securityMessage').textContent='Escaneá el QR, ingresá el código y confirmá.'}
  catch(x){$('securityMessage').textContent=`No se pudo activar 2FA: ${x.message}`;alert($('securityMessage').textContent)}finally{$('mfaEnable').disabled=false}
};

confirmMfa=async function(){
  const code=$('mfaCode').value.replace(/\D/g,'');if(code.length!==6){$('securityMessage').textContent='Ingresá los 6 dígitos del autenticador.';return}
  try{const x=await api('/api/account/mfa/verify',{method:'POST',body:JSON.stringify({accessToken:'',factorId:mfaFactorId,code})});auth=x;localStorage.setItem('ares.platform.auth',JSON.stringify(auth));$('mfaSetup').classList.add('hidden');$('securityMessage').textContent='2FA activado correctamente.';await showSecurity()}
  catch(x){$('securityMessage').textContent=`No se pudo confirmar 2FA: ${x.message}`;alert($('securityMessage').textContent)}
};
