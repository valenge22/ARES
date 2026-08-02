# Publicación de ARES Server

ARES Server debe publicarse en un alojamiento con HTTPS y almacenamiento persistente.
Los agentes y la consola administrativa deben usar exactamente la misma URL y clave.

## Variables requeridas

- `ARES_API_KEY`: una clave larga y aleatoria, diferente de `CAMBIAR-ESTA-CLAVE`.
- `ASPNETCORE_URLS=http://+:8080`: puerto interno del contenedor.

## Docker

Desde la raíz de la solución:

```powershell
docker build -f ARES.Server/Dockerfile -t ares-server .
docker run -d --name ares-server -p 8080:8080 -e ARES_API_KEY="SU-CLAVE-SEGURA" -v ares-data:/app/data ares-server
```

En producción, colocar el servicio detrás del HTTPS provisto por el alojamiento. No
se deben enviar heartbeats ni claves por HTTP público.

## Configuración de clientes

Actualizar `ServerUrl` y `ApiKey` en
`AdministracionEmpleados/appsettings.json`. Al instalar cada agente, `INSTALAR.bat`
solicita esos mismos dos valores.

Comprobar el servidor abriendo `https://SU-DOMINIO/health`; debe responder con
`{"service":"ARES Server","status":"ok"}`.
