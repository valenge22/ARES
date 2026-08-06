# Operación segura de ARES

## Secretos

Guardar únicamente en Render o Supabase, nunca en GitHub, instaladores, capturas o archivos compartidos:

- `ARES_API_KEY`
- `SUPABASE_SERVICE_ROLE_KEY`
- `SUPABASE_DB_CONNECTION`
- credenciales SMTP de `supportares@controlares.com`
- credenciales y secreto de webhook de Mercado Pago

Ante una filtración, rotar la clave afectada inmediatamente, redeployar Render y revocar las sesiones administrativas desde ARES.

## Copias de seguridad

Antes de una actualización importante o del lanzamiento:

1. En Supabase, realizar un backup/export de la base de datos desde Database/Backups o mediante `pg_dump` usando la conexión directa.
2. Guardar el archivo cifrado fuera de Render y con fecha en el nombre.
3. Conservar una copia de los instaladores publicados y sus versiones.
4. Probar una restauración en un proyecto Supabase de prueba antes de depender de la copia.

Los datos de producción no deben restaurarse sobre el proyecto activo sin una copia reciente y una ventana de mantenimiento.

## Dominio y correo

- Mantener HTTPS activo en `controlares.com`.
- Verificar MX, SPF, DKIM y DMARC de Hostinger antes de modificar DNS.
- Usar una casilla exclusiva para ARES y una contraseña única.

## Antes de publicar una actualización

1. Compilar y probar el servidor.
2. Probar login, 2FA, creación de organización y enlace de un Agent.
3. Verificar que el Portal no mezcle datos entre organizaciones.
4. Publicar instaladores firmados cuando se adquiera un certificado de firma de código.
