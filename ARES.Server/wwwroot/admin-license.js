const aresPlans={Trial:{name:'Prueba',devices:5,users:1,base:0,device:0,user:0},Basic:{name:'Esencial',devices:10,users:2,base:25,device:3,user:4},Professional:{name:'Profesional',devices:30,users:10,base:65,device:2.5,user:3},Business:{name:'Empresa',devices:100,users:25,base:149,device:2,user:2},Enterprise:{name:'Corporativo',devices:100,users:25,base:249,device:2,user:2}};
const aresStatus={Active:'Activa',Suspended:'Suspendida',Expired:'Vencida',Canceled:'Cancelada',PastDue:'Pago pendiente'};

document.addEventListener('DOMContentLoaded',()=>{
  $('editPlan').innerHTML=Object.entries(aresPlans).map(([value,x])=>`<option value="${value}">${x.name}</option>`).join('');
  $('editStatus').innerHTML=Object.entries(aresStatus).map(([value,name])=>`<option value="${value}">${name}</option>`).join('');
  const maxRow=$('editMax').closest('.row');
  maxRow.insertAdjacentHTML('afterend','<div class="row"><div><label>Equipos adicionales</label><input id="editAdditionalDevices" type="number" min="0" max="100000" value="0"></div><div><label>Usuarios adicionales</label><input id="editAdditionalUsers" type="number" min="0" max="10000" value="0"></div></div><div id="licenseSummary" class="muted" style="padding:14px 0"></div>');
  $('editMax').closest('div').insertAdjacentHTML('afterend','<div><label>Usuarios incluidos</label><input id="editMaxUsers" type="number" min="1" max="100000"></div>');
  [$('editPlan'),$('editAdditionalDevices'),$('editAdditionalUsers')].forEach(x=>x.addEventListener('input',updateLicenseSummary));
});

function updateLicenseSummary(){const p=aresPlans[$('editPlan').value]||aresPlans.Trial,extraDevices=+$('editAdditionalDevices').value||0,extraUsers=+$('editAdditionalUsers').value||0;$('editMax').value=p.devices;$('editMaxUsers').value=p.users;const total=p.base+extraDevices*p.device+extraUsers*p.user;$('licenseSummary').innerHTML=`Incluye <b>${p.devices} equipos</b> y <b>${p.users} usuarios</b>.<br>Total: <b>USD ${total.toFixed(2)} por mes</b>`}

loadOrganizations=async function(){items=await api('/api/platform/organizations');$('orgCount').textContent=items.length;$('deviceCount').textContent=items.reduce((n,x)=>n+x.usedDevices,0);$('trialCount').textContent=items.filter(x=>x.plan==='Trial'&&x.status==='Active').length;$('organizations').innerHTML=items.map((x,i)=>`<tr><td><b>${esc(x.organizationName)}</b><br><span class="muted">${esc(x.slug)}</span></td><td>${esc(x.planName||aresPlans[x.plan]?.name||x.plan)}<br><span class="muted">USD ${Number(x.monthlyPriceUsd||0).toFixed(2)}/mes</span></td><td>${esc(x.statusName||aresStatus[x.status]||x.status)}</td><td>${x.usedDevices}/${x.totalDevices}<br><span class="muted">Usuarios: ${x.usedPanelUsers}/${x.totalPanelUsers}</span></td><td>${x.expiresAt?new Date(x.expiresAt).toLocaleDateString('es-AR'):x.plan==='Trial'?new Date(x.trialEndsAt).toLocaleDateString('es-AR'):'Sin vencimiento'}</td><td class="actions"><button class="secondary" onclick="openEditor(${i})">Editar</button> <button class="danger" onclick="removeOrg('${x.organizationId}')">Eliminar</button></td></tr>`).join('')};
openEditor=function(i){const x=items[i];$('editId').value=x.organizationId;$('editPlan').value=x.plan;$('editStatus').value=x.status;$('editMax').value=x.maxDevices;$('editMaxUsers').value=x.maxPanelUsers;$('editAdditionalDevices').value=x.additionalDevices;$('editAdditionalUsers').value=x.additionalPanelUsers;$('editGrace').value=x.graceDays;$('editExpires').value=x.expiresAt?x.expiresAt.slice(0,10):'';$('editError').textContent='';updateLicenseSummary();$('editor').showModal()};
saveLicense=async function(e){e.preventDefault();try{await api(`/api/platform/organizations/${$('editId').value}/license`,{method:'PUT',body:JSON.stringify({plan:$('editPlan').value,status:$('editStatus').value,maxDevices:+$('editMax').value,maxPanelUsers:+$('editMaxUsers').value,additionalDevices:+$('editAdditionalDevices').value,additionalPanelUsers:+$('editAdditionalUsers').value,graceDays:+$('editGrace').value,expiresAt:$('editExpires').value?new Date($('editExpires').value+'T23:59:59-03:00').toISOString():null})});$('editor').close();await loadOrganizations()}catch(x){$('editError').textContent=x.message}};
const operationsScript=document.createElement('script');operationsScript.src='/admin-operations.js';operationsScript.defer=true;document.head.appendChild(operationsScript);
const downloadsScript=document.createElement('script');downloadsScript.src='/admin-downloads.js';downloadsScript.defer=true;document.head.appendChild(downloadsScript);

document.addEventListener('DOMContentLoaded',()=>{
  const update=[...document.querySelectorAll('button')].find(x=>x.textContent.trim()==='Actualizar'&&x.getAttribute('onclick')==='loadOrganizations()');
  if(update){const plans=document.createElement('button');plans.className='secondary';plans.textContent='Configurar planes';plans.onclick=managePlans;update.parentElement.insertBefore(plans,update)}
});

async function managePlans(){
  try{
    const plans=await api('/api/platform/plans');
    const dialog=document.createElement('dialog');dialog.style.width='min(930px,96vw)';
    dialog.innerHTML='<form method="dialog"><h2>Planes comerciales</h2><p class="muted">Los cambios se aplican a nuevas compras. No modifican automáticamente contratos ya activos.</p><div class="table" style="overflow:auto"><table><thead><tr><th>Plan</th><th>Nombre visible</th><th>Equipos incluidos</th><th>Usuarios incluidos</th><th>USD/mes</th><th>USD/equipo extra</th><th>USD/usuario extra</th><th>Disponible</th><th></th></tr></thead><tbody></tbody></table></div><div style="display:flex;justify-content:flex-end;margin-top:20px"><button>Cerrar</button></div></form>';
    const body=dialog.querySelector('tbody');
    plans.forEach(plan=>{
      const row=document.createElement('tr'); const code=esc(plan.code);
      row.innerHTML=`<td><b>${code}</b></td><td><input value="${esc(plan.displayName)}" maxlength="40"></td><td><input type="number" min="0" max="100000" value="${plan.includedDevices}"></td><td><input type="number" min="0" max="10000" value="${plan.includedPanelUsers}"></td><td><input type="number" min="0" step="0.01" value="${plan.monthlyPriceUsd}"></td><td><input type="number" min="0" step="0.01" value="${plan.additionalDeviceUsd}"></td><td><input type="number" min="0" step="0.01" value="${plan.additionalPanelUserUsd}"></td><td><input type="checkbox" ${plan.available?'checked':''} ${plan.code==='Trial'?'disabled':''}></td><td><button type="button" class="primary">Guardar</button></td>`;
      const input=row.querySelectorAll('input'), save=row.querySelector('button');
      save.onclick=async()=>{save.disabled=true;try{const x={displayName:input[0].value,includedDevices:+input[1].value,includedPanelUsers:+input[2].value,monthlyPriceUsd:+input[3].value,additionalDeviceUsd:+input[4].value,additionalPanelUserUsd:+input[5].value,available:input[6].checked};const updated=await api(`/api/platform/plans/${encodeURIComponent(plan.code)}`,{method:'PUT',body:JSON.stringify(x)});aresPlans[updated.code]={name:updated.displayName,devices:updated.includedDevices,users:updated.includedPanelUsers,base:updated.monthlyPriceUsd,device:updated.additionalDeviceUsd,user:updated.additionalPanelUserUsd};save.textContent='Guardado';setTimeout(()=>save.textContent='Guardar',1200)}catch(e){alert(e.message)}finally{save.disabled=false}};
      body.appendChild(row);
    });
    document.body.appendChild(dialog);dialog.addEventListener('close',()=>dialog.remove());dialog.showModal();
  }catch(e){alert(e.message)}
}
