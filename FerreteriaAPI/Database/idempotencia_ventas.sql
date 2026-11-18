-- Claves de idempotencia de POST api/Ventas (cabecera X-Idempotency-Key).
-- La API ejecuta este mismo script al arrancar (IdempotenciaVentas.SqlCrearTabla), así que
-- no hace falta correrlo a mano; queda aquí como referencia del esquema.
CREATE TABLE IF NOT EXISTS idempotencia_ventas (
    clave          uuid        PRIMARY KEY,
    huella         text        NOT NULL,
    id_venta       integer     NULL REFERENCES ventas(id) ON DELETE CASCADE,
    codigo_estado  integer     NULL,
    respuesta      text        NULL,
    creado_en      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_idempotencia_ventas_creado_en ON idempotencia_ventas (creado_en);

-- Opcional (Supabase con pg_cron): limpieza en la base aunque la API esté dormida en Render.
-- SELECT cron.schedule('limpiar-idempotencia', '*/10 * * * *',
--     $$DELETE FROM idempotencia_ventas WHERE creado_en < now() - interval '15 minutes'$$);
