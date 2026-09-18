import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
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
export class MarketingModulesComponent implements OnInit, OnDestroy {
  private route=inject(ActivatedRoute); private svc=inject(MarketingService); mode='';q='';district='';prospects=signal<any[]>([]);campaigns=signal<any[]>([]);drafts=signal<any[]>([]);followups=signal<any[]>([]);summary=signal<any>(null);agent=signal<any>(null);showForm=signal(false);
  campaign:any={nombre:'',audiencia:'',canal:'EMAIL',asunto:'',mensaje:''};draft:any={canal:'EMAIL',destinatario:'',asunto:'',cuerpo:''};google=signal<any>(null);exporting=signal(false);agentStatus=signal<any>(null);private timer:any;
  pub:any={tipo:'promo',negocio:'LunaLav',oferta:'',zona:'',contacto:'',emojis:true};postText='';copiado=signal(false);
  private readonly jobNames:Record<string,string>={MARKETING_SCORE_RECALCULATION:'Recalcular scores',MARKETING_FOLLOWUP_REVIEW:'Revisar seguimientos vencidos',MARKETING_PREPARE_OUTREACH:'Preparar contactos'};
  jobLabel(t:string){return this.jobNames[t]||t.replaceAll('_',' ');}
  loadAgentStatus(){this.svc.agentStatus().subscribe(x=>this.agentStatus.set(x));}
  runNow(type:string){this.svc.runAutomation(type).subscribe(()=>setTimeout(()=>this.loadAgentStatus(),1500));}
  ngOnDestroy(){if(this.timer)clearInterval(this.timer);}
  ngOnInit(){this.mode=this.route.snapshot.data['mode'];if(this.mode==='explorar')this.loadExplore();if(this.mode==='campanas')this.loadCampaigns();if(this.mode==='inbox')this.loadDrafts();if(this.mode==='calendario')this.svc.followups().subscribe(x=>this.followups.set(x));if(this.mode==='reportes')this.svc.dashboard().subscribe(x=>this.summary.set(x));if(this.mode==='agentes'){this.svc.agentSettings().subscribe(x=>this.agent.set(x));this.loadAgentStatus();this.timer=setInterval(()=>this.loadAgentStatus(),30000);}if(this.mode==='configuracion')this.svc.googleStatus().subscribe(x=>this.google.set(x));if(this.mode==='publicaciones')this.generarPost();}
  generarPost(){const e=this.pub.emojis;const n=(this.pub.negocio||'nuestra lavandería').trim();const o=(this.pub.oferta||'').trim();const z=(this.pub.zona||'').trim();const c=(this.pub.contacto||'').trim();let cuerpo='';
    switch(this.pub.tipo){
      case 'promo':cuerpo=`${e?'🧺✨ ':''}¡${o||'Oferta especial'} en ${n}!\n\nRopa impecable sin mover un dedo. Aprovecha ${o?('esta promo: '+o):'nuestras promociones'}${z?(' en '+z):''}.`;break;
      case 'novedad':cuerpo=`${e?'🎉 ':''}¡Novedades en ${n}!\n\n${o||'Tenemos algo nuevo para ti'}.${z?(' Te esperamos en '+z+'.'):''}`;break;
      case 'consejo':cuerpo=`${e?'💡 ':''}Tip de lavandería\n\n${o||'Separa la ropa por colores antes de lavar para que los tejidos duren más.'}\n\nEn ${n} lo hacemos por ti.`;break;
      default:cuerpo=`${e?'⭐⭐⭐⭐⭐ ':''}"${o||'Excelente servicio, mi ropa quedó como nueva.'}"\n\nGracias por confiar en ${n}.${z?(' ¡Te esperamos en '+z+'!'):''}`;
    }
    const cta=c?`\n\n${e?'📲 ':''}Escríbenos: ${c}`:'';
    this.postText=`${cuerpo}${cta}\n\n${this.hashtags()}`.replace(/\n{3,}/g,'\n\n').trim();this.copiado.set(false);}
  private hashtags(){const t=['#Lavanderia','#RopaLimpia',`#${(this.pub.negocio||'LunaLav').replace(/[^\p{L}\p{N}]/gu,'')||'LunaLav'}`];const z=(this.pub.zona||'').replace(/[^\p{L}\p{N}]/gu,'');if(z)t.push('#'+z);if(this.pub.tipo==='promo')t.push('#Promo','#Descuento');if(this.pub.tipo==='consejo')t.push('#Tips');return t.join(' ');}
  copiarPost(){const done=()=>{this.copiado.set(true);setTimeout(()=>this.copiado.set(false),2000);};if(navigator.clipboard?.writeText){navigator.clipboard.writeText(this.postText).then(done).catch(()=>this.fallbackCopy(done));}else this.fallbackCopy(done);}
  private fallbackCopy(done:()=>void){const ta=document.createElement('textarea');ta.value=this.postText;document.body.appendChild(ta);ta.select();try{document.execCommand('copy');done();}catch{}document.body.removeChild(ta);}
  waShare(){return 'https://wa.me/?text='+encodeURIComponent(this.postText);}
  fbShare(){return 'https://www.facebook.com/sharer/sharer.php?u='+encodeURIComponent('https://lunalav.pe')+'&quote='+encodeURIComponent(this.postText);}
  twShare(){return 'https://twitter.com/intent/tweet?text='+encodeURIComponent(this.postText);}
  loadExplore(){this.svc.prospects({q:this.q,distrito:this.district,pageSize:100}).subscribe(x=>this.prospects.set(x.items));} loadCampaigns(){this.svc.campaigns().subscribe(x=>this.campaigns.set(x));} loadDrafts(){this.svc.drafts().subscribe(x=>this.drafts.set(x));}
  createCampaign(){this.svc.createCampaign(this.campaign).subscribe({next:()=>{this.campaign={nombre:'',audiencia:'',canal:'EMAIL',asunto:'',mensaje:''};this.showForm.set(false);this.loadCampaigns();}});}campaignState(id:number,estado:string){this.svc.campaignStatus(id,estado).subscribe(()=>this.loadCampaigns());}
  createDraft(){this.svc.createDraft(this.draft).subscribe({next:()=>{this.draft={canal:'EMAIL',destinatario:'',asunto:'',cuerpo:''};this.showForm.set(false);this.loadDrafts();}});}draftState(id:number,estado:string){this.svc.draftStatus(id,estado).subscribe(()=>this.loadDrafts());}connectGmail(){this.svc.startGmailConnect().subscribe({next:x=>location.assign(x.authorizationUrl),error:e=>alert(e.error?.mensaje||'No se pudo iniciar Gmail.')});}saveAgent(){this.svc.saveAgentSettings(this.agent()).subscribe();}ratio(n:number){return this.summary()?.prospectosTotales?Math.max(8,Math.round(n*100/this.summary().prospectosTotales)):8;}
  pct(a:number,b:number){return b?Math.round((a||0)*100/b):0;}
  exportCsv(){this.exporting.set(true);this.svc.prospects({pageSize:2000}).subscribe({next:(x:any)=>{const rows=x.items||[];const head=['Negocio','Distrito','Telefono','WhatsApp','Email','Estado','Prioridad','Score','Fuente','UltimoContacto','ProximoSeguimiento','Etiquetas'];const esc=(v:any)=>{const s=v==null?'':String(v);return /[";\n]/.test(s)?'"'+s.replace(/"/g,'""')+'"':s;};const lines=[head.join(';')];for(const r of rows)lines.push([r.nombreComercial,r.distrito,r.telefono,r.whatsapp,r.email,r.estado,r.prioridad,r.score,r.fuente,r.fechaUltimoContacto?new Date(r.fechaUltimoContacto).toLocaleDateString():'',r.proximoSeguimiento?new Date(r.proximoSeguimiento).toLocaleDateString():'',(r.tags||[]).map((t:any)=>t.nombre).join(' | ')].map(esc).join(';'));const blob=new Blob(['﻿'+lines.join('\r\n')],{type:'text/csv;charset=utf-8'});const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download=`prospectos-lunalav-${new Date().toISOString().slice(0,10)}.csv`;a.click();URL.revokeObjectURL(url);this.exporting.set(false);},error:()=>this.exporting.set(false)});}
}
