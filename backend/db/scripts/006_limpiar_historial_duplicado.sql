-- ============================================================
-- Limpia entradas duplicadas del historial de pedidos
-- Duplicado = mismo pedido + misma area + mismo estado + mismo minuto
-- Deja solo la más antigua de cada grupo.
-- SEGURO: no toca los pedidos, solo el historial.
-- ============================================================
USE LunaLav;
GO

WITH Duplicados AS (
    SELECT
        Id,
        ROW_NUMBER() OVER (
            PARTITION BY
                PedidoId,
                ISNULL(AreaId, -1),
                EstadoProceso,
                DATEADD(MINUTE, DATEDIFF(MINUTE, 0, Fecha), 0)  -- redondea al minuto
            ORDER BY Fecha ASC, Id ASC
        ) AS rn
    FROM dbo.PedidoHistorial
)
DELETE FROM Duplicados WHERE rn > 1;

PRINT CAST(@@ROWCOUNT AS VARCHAR(10)) + ' entrada(s) duplicada(s) eliminada(s) del historial.';
GO

