-- 033: Textos demostrativos para tickets y mensajes de WhatsApp.
-- No contiene direcciones, teléfonos ni condiciones de un cliente real.
DECLARE @Condiciones NVARCHAR(MAX) = N'Revisa los bolsillos antes de entregar las prendas.
Las manchas difíciles y prendas delicadas se atienden con cuidado, pero pueden requerir una evaluación previa.
Conserva tu comprobante para recoger el pedido y revisa las prendas antes de retirarte.
Este texto es demostrativo y cada lavandería debe reemplazarlo por sus propias condiciones de servicio.';

UPDATE c
   SET Direccion = N'Av. Ejemplo 123, Lima',
       Telefono = N'999 000 000',
       HorarioAtencion = N'Lun a Sáb: 8:00 am - 7:00 pm',
       MensajePieTicket = N'Gracias por probar LunaLav.',
       CondicionesServicio = @Condiciones
  FROM dbo.ConfiguracionNegocio c
  JOIN dbo.Negocio n ON n.Id = c.NegocioId
 WHERE n.Slug = 'demo';

DECLARE @PlantillaIngreso NVARCHAR(MAX) = N'¡Hola *{cliente}*!
La lavandería *{negocio}* recibió tu orden *{numero}*:

{items}

Total: *S/ {total}* · Saldo: *S/ {saldo}*
Entrega estimada: *{entrega}*

{seguimiento}

*CONDICIONES DEL SERVICIO*
{condiciones}';

UPDATE p
   SET Mensaje = @PlantillaIngreso,
       Activa = 1
  FROM dbo.PlantillaWhatsapp p
  JOIN dbo.Negocio n ON n.Id = p.NegocioId
 WHERE p.Evento = 'INGRESO'
   AND n.Slug = 'demo';
