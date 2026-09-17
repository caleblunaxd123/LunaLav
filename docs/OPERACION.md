# Operación local de LunaLav

## Servicios

- Web comercial y demo: `http://127.0.0.1:5005`
- Acceso de clientes: `http://127.0.0.1:5004/login`
- Cloudflare Tunnel se ejecuta como servicio de Windows.

La tarea programada **LunaLav - Servicios** inicia los puertos 5004 y 5005 cuando Caleb inicia sesión en Windows. Para reconstruir y reiniciar una versión nueva:

```powershell
.\scripts\iniciar-demo-lunalav.ps1
```

Para reiniciar únicamente la versión ya publicada:

```powershell
.\scripts\iniciar-runtime-lunalav.ps1
```

## Respaldos

La tarea **LunaLav - Respaldo diario** se ejecuta todos los días a las 03:00, o al volver a estar disponible el equipo. Cada copia se verifica con SQL Server y se conserva durante 14 días en el directorio exclusivo de respaldos de la instancia SQL.

Para crear un respaldo manual:

```powershell
.\scripts\respaldo-lunalav.ps1
```

## Después de reiniciar Windows

1. Iniciar sesión con el usuario habitual.
2. Confirmar que la PC esté conectada a corriente e Internet.
3. Probar `http://127.0.0.1:5005/` y `http://127.0.0.1:5004/login`.
4. Si no responden, ejecutar `scripts\iniciar-runtime-lunalav.ps1`.

## Dependencias del equipo

- SQL Server Express y `sqlcmd`.
- .NET 9.
- Node.js y npm para construir versiones nuevas.
- Servicio `cloudflared` para el acceso público.

No mover ni publicar los secretos guardados en `%LOCALAPPDATA%\LunaLav\demo`.
