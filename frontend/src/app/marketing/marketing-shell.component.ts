import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MarketingAuthService } from './marketing-auth.service';

@Component({
  standalone:true,
  imports:[RouterLink,RouterLinkActive,RouterOutlet],
  template:`<div class="shell">
    <aside class="sidebar">
      <a class="brand" routerLink="/marketing"><img src="/lunalav-logo-color.svg" alt="LunaLav Marketing"><small>MARKETING</small></a>
      <nav aria-label="Navegación de Marketing">
        <a routerLink="/marketing" routerLinkActive="active" [routerLinkActiveOptions]="{exact:true}"><i>⌂</i> Inicio</a>
        <a routerLink="/marketing/prospectos" routerLinkActive="active"><i>♙</i> Prospectos</a>
        <a class="future"><i>⌕</i> Explorar <em>Próximamente</em></a>
        <a class="future"><i>⚑</i> Campañas <em>Próximamente</em></a>
        <a class="future"><i>✉</i> Inbox <em>Próximamente</em></a>
        <a routerLink="/marketing/seguimientos" routerLinkActive="active"><i>☑</i> Tareas y seguimientos</a>
        <a class="future"><i>▣</i> Calendario <em>Próximamente</em></a>
        <a class="future"><i>▥</i> Reportes <em>Próximamente</em></a>
      </nav>
      <div class="side-bottom"><a class="future"><i>✧</i> Agentes IA <em>Próximamente</em></a><a class="future"><i>⚙</i> Configuración <em>Próximamente</em></a></div>
      <div class="profile"><span class="avatar">{{inicial}}</span><div><b>{{auth.usuario()?.nombre || 'Equipo LunaLav'}}</b><small>{{auth.usuario()?.rol || 'Marketing'}}</small></div><button (click)="auth.logout()" title="Cerrar sesión">⋮</button></div>
    </aside>
    <section class="workspace"><header class="topbar"><label class="search"><i>⌕</i><input placeholder="Buscar en LunaLav…" aria-label="Buscar en LunaLav"></label><div class="top-actions"><button class="notification" title="Notificaciones">♧<sup>0</sup></button><span class="top-avatar">{{inicial}}</span><div class="hello"><b>Hola, {{primerNombre}}</b><small>Marketing</small></div><span class="chevron">⌄</span></div></header><main><router-outlet/></main></section>
  </div>`,
  styles:[`.shell{min-height:100vh;display:grid;grid-template-columns:220px minmax(0,1fr);background:#eef8ff;color:#102f64;font-family:Inter,Arial,sans-serif}.sidebar{z-index:2;background:#fff;border-right:1px solid #dceefa;box-shadow:4px 0 18px #19629e0d;display:flex;flex-direction:column;padding:15px 10px}.brand{height:64px;display:flex;align-items:center;gap:4px;padding:0 16px;text-decoration:none;border-bottom:1px solid #edf5fb}.brand img{width:126px;height:45px;object-fit:contain;object-position:left}.brand small{font-size:8px;letter-spacing:1.2px;color:#466d9e;font-weight:900;margin-left:-48px;margin-top:35px}nav{display:grid;gap:4px;padding:20px 6px}.sidebar a{position:relative;color:#547099;text-decoration:none;padding:11px 12px;border-radius:9px;font-size:13px;font-weight:700;display:flex;align-items:center;gap:13px;cursor:pointer}.sidebar a i{font-style:normal;color:#3a68b7;font-size:19px;width:19px;text-align:center}.sidebar a.active{background:#e1f4ff;color:#1388e5}.sidebar a.active:before{content:'';position:absolute;width:3px;height:22px;left:-10px;border-radius:4px;background:#168ff1}.sidebar a.future{opacity:.68}.sidebar a em{display:none;font-style:normal;font-size:8px;background:#f3f7fb;padding:3px 5px;border-radius:5px;margin-left:auto}.side-bottom{margin-top:auto;padding:10px 6px;border-top:1px solid #edf4fa}.profile{display:flex;align-items:center;gap:9px;border-top:1px solid #edf4fa;padding:16px 10px 4px}.avatar,.top-avatar{display:grid;place-items:center;background:#177de1;color:white;border-radius:50%;width:34px;height:34px;font-size:13px;font-weight:900}.profile div{display:grid;gap:2px;min-width:0;flex:1}.profile b{font-size:12px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.profile small,.hello small{font-size:10px;color:#7891ad}.profile button{border:0;background:transparent;color:#547099;font-size:20px;cursor:pointer}.workspace{min-width:0}.topbar{height:70px;display:flex;align-items:center;justify-content:space-between;padding:0 30px;background:#fff;border-bottom:1px solid #dceefa}.search{display:flex;align-items:center;gap:9px;width:min(380px,55vw);height:38px;padding:0 13px;background:#f2f8fd;border:1px solid #dbeefa;border-radius:9px;color:#6f93b8}.search i{font-size:20px;font-style:normal}.search input{width:100%;border:0;outline:0;background:transparent;color:#32516f;font-size:13px}.top-actions{display:flex;align-items:center;gap:10px}.notification{position:relative;border:0;background:transparent;font-size:21px;color:#2c5aa5}.notification sup{position:absolute;right:-3px;top:-5px;background:#ff5163;color:#fff;border-radius:50%;font:700 8px Arial;padding:2px}.hello{display:grid;gap:2px;font-size:12px}.chevron{color:#6784a6}main{padding:29px;max-width:1580px;margin:0 auto}@media(max-width:850px){.shell{grid-template-columns:1fr}.sidebar{display:none}.topbar{padding:0 16px}main{padding:18px}.hello,.chevron{display:none}.search{width:70vw}}`]
})
export class MarketingShellComponent {
  readonly auth=inject(MarketingAuthService);
  get primerNombre(){return (this.auth.usuario()?.nombre || 'Equipo').split(' ')[0];}
  get inicial(){return this.primerNombre.charAt(0).toUpperCase();}
}
