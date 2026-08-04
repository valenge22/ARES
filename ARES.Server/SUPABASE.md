# Configurar Supabase para ARES

ARES sigue ejecutando su API en Render. Supabase se utiliza como PostgreSQL
persistente; los agentes y paneles no se conectan directamente a Supabase.

1. En Supabase, abrir **Connect** y copiar la cadena de conexión del pooler en
   modo **Session**. Para Render conviene la dirección IPv4 del pooler.
2. Reemplazar el marcador de contraseña por la contraseña de la base de datos.
   ARES acepta tanto la URL `postgresql://...` copiada de Supabase como una
   cadena de parámetros compatible con Npgsql.
3. En Render, abrir el servicio de ARES y agregar un Secret/Environment Variable:

   - Nombre: `SUPABASE_DB_CONNECTION`
   - Valor: la cadena completa copiada desde Supabase

4. Desplegar nuevamente el servicio.
5. Consultar `/health`. Al iniciar, ARES crea automáticamente la tabla
   `public.ares_state` y la tabla de administradores `public.ares_admin_users`.
   ARES activa RLS en ambas tablas y retira el acceso directo de los roles
   publicos `anon` y `authenticated`; solamente el backend accede por PostgreSQL.

## Crear el propietario inicial

Crear el usuario en **Supabase > Authentication > Users** y agregar en Render:

- `ARES_OWNER_USER_ID`: UUID del usuario creado en Supabase Auth.
- `ARES_OWNER_NAME`: nombre visible del propietario.

Al desplegar, ARES registra o actualiza ese usuario con el rol `Owner`. La
contraseña permanece exclusivamente en Supabase Auth.

No guardar la cadena de conexión en `appsettings.json`, instaladores, capturas ni
GitHub. Si la variable no está configurada, ARES conserva el almacenamiento JSON
local para desarrollo.
