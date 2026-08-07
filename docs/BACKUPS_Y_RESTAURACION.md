# Backups de ARES

ARES genera un respaldo lógico cifrado de Supabase cada día mediante GitHub Actions y lo conserva durante 30 días como artefacto privado.

## Configuración inicial

En el repositorio GitHub de ARES, ir a **Settings → Secrets and variables → Actions** y crear:

| Secreto | Valor |
|---|---|
| `SUPABASE_BACKUP_CONNECTION` | Cadena de conexión PostgreSQL de Supabase apta para `pg_dump`. Preferir la conexión directa o pooler en modo sesión. |
| `ARES_BACKUP_PASSPHRASE` | Contraseña larga, única y guardada en un gestor de contraseñas. Sin ella no se puede restaurar el backup. |

Luego abrir **Actions → Respaldo cifrado de Supabase → Run workflow** para ejecutar y validar el primer backup.

## Recuperar un respaldo

1. Descargar el artefacto desde la ejecución correspondiente de GitHub Actions.
2. Verificar la integridad: `sha256sum -c ares-supabase.dump.gpg.sha256`.
3. Descifrar: `gpg --batch --decrypt --passphrase "TU_CLAVE" --output ares-supabase.dump ares-supabase.dump.gpg`.
4. Restaurar **primero en un proyecto Supabase de prueba**, nunca directamente en producción: `pg_restore --clean --if-exists --no-owner --dbname "POSTGRES_URL_DE_PRUEBA" ares-supabase.dump`.
5. Validar usuarios, organizaciones, licencias y equipos antes de planificar una restauración productiva.

## Reglas operativas

- Probar una restauración al menos cada tres meses.
- Guardar una copia externa cifrada mensual fuera de GitHub.
- Si se filtra la cadena de conexión, rotar la contraseña de PostgreSQL y actualizar el secreto.
- Los backups de base de datos no incluyen necesariamente archivos que se almacenen en servicios de Storage externos.
