# LunaLav

SaaS de gestión para lavanderías: pedidos, caja, clientes, inventario, reparto,
reportes, facturación y administración multiempresa.

## Dominios

- `lunalav.pe`: web comercial y captación de clientes.
- `demo.lunalav.pe`: demostración pública con datos ficticios.
- `app.lunalav.pe`: aplicación de producción.
- Cada cliente usa un slug propio, por ejemplo:
  `https://app.lunalav.pe/lavanderiamemo/login`.

La demo y producción deben usar bases de datos, secretos y procesos separados.
Nunca se deben copiar datos, archivos ni credenciales de un cliente a la demo.

## Arquitectura

- Frontend: Angular.
- Backend: ASP.NET Core / .NET 9.
- Base de datos: SQL Server (`LunaLav`).
- Acceso público: Cloudflare Tunnel, sin abrir puertos entrantes del router.

## Desarrollo local

Requisitos: .NET 9 SDK, Node.js, SQL Server Express y `sqlcmd`.

1. Ejecuta, en orden, los scripts de `backend/db/scripts`.
2. Inicia la API:

   ```powershell
   dotnet run --project backend/src/Lavanderia.Api
   ```

3. Inicia Angular:

   ```powershell
   npm install --prefix frontend
   npm start --prefix frontend
   ```

4. Abre `http://localhost:4200/demo/login`.

Las contraseñas y secretos reales se suministran mediante variables de entorno;
no se guardan en Git.

## Demo local/publicada

El script siguiente prepara la base aislada, compila el proyecto y levanta la
demo en el puerto local `5005`:

```powershell
.\scripts\iniciar-demo-lunalav.ps1
```

Con la ruta de Cloudflare configurada, la entrada pública será:

`https://demo.lunalav.pe/demo/login`

Los secretos de la demo se guardan fuera del repositorio, dentro de
`%LOCALAPPDATA%\LunaLav\demo`.

## Marca

La identidad principal de LunaLav combina:

- azul noche `#10233F`;
- turquesa `#2DD4BF`;
- violeta `#8B5CF6`.

Cada lavandería puede configurar su propio nombre, logotipo y colores sin
alterar la identidad del producto ni la información de otros clientes.
