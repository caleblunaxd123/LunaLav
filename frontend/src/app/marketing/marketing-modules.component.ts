import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MarketingService } from './marketing.service';

@Component({
  standalone:true,
  imports:[FormsModule,RouterLink,DatePipe],
  templateUrl:'./marketing-modules.component.html',
  styleUrls:['./marketing-modules.component.css']
})
export class MarketingModulesComponent implements OnInit {
  private route=inject(ActivatedRoute); private svc=inject(MarketingService); mode='';q='';district='';prospects=signal<any[]>([]);campaigns=signal<any[]>([]);drafts=signal<any[]>([]);followups=signal<any[]>([]);summary=signal<any>(null);agent=signal<any>(null);showForm=signal(false);
  campaign:any={nombre:'',audiencia:'',canal:'EMAIL',asunto:'',mensaje:''};draft:any={canal:'EMAIL',destinatario:'',asunto:'',cuerpo:''};google=signal<any>(null);exporting=signal(false);
  ngOnInit(){this.mode=this.route.snapshot.data['mode'];if(this.mode==='explorar')this.loadExplore();if(this.mode==='campanas')this.loadCampaigns();if(this.mode==='inbox')this.loadDrafts();if(this.mode==='calendario')this.svc.followups().subscribe(x=>this.followups.set(x));if(this.mode==='reportes')this.svc.dashboard().subscribe(x=>this.summary.set(x));if(this.mode==='agentes')this.svc.agentSettings().subscribe(x=>this.agent.set(x));if(this.mode==='configuracion')this.svc.googleStatus().subscribe(x=>this.google.set(x));}
  loadExplore(){this.svc.prospects({q:this.q,distrito:this.district,pageSize:100}).subscribe(x=>this.prospects.set(x.items));} loadCampaigns(){this.svc.campaigns().subscribe(x=>this.campaigns.set(x));} loadDrafts(){this.svc.drafts().subscribe(x=>this.drafts.set(x));}
  createCampaign(){this.svc.createCampaign(this.campaign).subscribe({next:()=>{this.campaign={nombre:'',audiencia:'',canal:'EMAIL',asunto:'',mensaje:''};this.showForm.set(false);this.loadCampaigns();}});}campaignState(id:number,estado:string){this.svc.campaignStatus(id,estado).subscribe(()=>this.loadCampaigns());}
  createDraft(){this.svc.createDraft(this.draft).subscribe({next:()=>{this.draft={canal:'EMAIL',destinatario:'',asunto:'',cuerpo:''};this.showForm.set(false);this.loadDrafts();}});}draftState(id:number,estado:string){this.svc.draftStatus(id,estado).subscribe(()=>this.loadDrafts());}connectGmail(){this.svc.startGmailConnect().subscribe({next:x=>location.assign(x.authorizationUrl),error:e=>alert(e.error?.mensaje||'No se pudo iniciar Gmail.')});}saveAgent(){this.svc.saveAgentSettings(this.agent()).subscribe();}ratio(n:number){return this.summary()?.prospectosTotales?Math.max(8,Math.round(n*100/this.summary().prospectosTotales)):8;}
  pct(a:number,b:number){return b?Math.round((a||0)*100/b):0;}
  exportCsv(){this.exporting.set(true);this.svc.prospects({pageSize:2000}).subscribe({next:(x:any)=>{const rows=x.items||[];const head=['Negocio','Distrito','Telefono','WhatsApp','Email','Estado','Prioridad','Score','Fuente','UltimoContacto','ProximoSeguimiento','Etiquetas'];const esc=(v:any)=>{const s=v==null?'':String(v);return /[";\n]/.test(s)?'"'+s.replace(/"/g,'""')+'"':s;};const lines=[head.join(';')];for(const r of rows)lines.push([r.nombreComercial,r.distrito,r.telefono,r.whatsapp,r.email,r.estado,r.prioridad,r.score,r.fuente,r.fechaUltimoContacto?new Date(r.fechaUltimoContacto).toLocaleDateString():'',r.proximoSeguimiento?new Date(r.proximoSeguimiento).toLocaleDateString():'',(r.tags||[]).map((t:any)=>t.nombre).join(' | ')].map(esc).join(';'));const blob=new Blob(['﻿'+lines.join('\r\n')],{type:'text/csv;charset=utf-8'});const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download=`prospectos-lunalav-${new Date().toISOString().slice(0,10)}.csv`;a.click();URL.revokeObjectURL(url);this.exporting.set(false);},error:()=>this.exporting.set(false)});}
}
