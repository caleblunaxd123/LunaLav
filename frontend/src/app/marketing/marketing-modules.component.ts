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
  pub:any={tipo:'promo',temporada:'ninguna',negocio:'LunaLav',oferta:'',zona:'',contacto:'',emojis:true,modelo:'qwen3:8b'};postText='';
  readonly modelos:{id:string,label:string}[]=[{id:'qwen3:8b',label:'Qwen3 8B · mejor redacción'},{id:'llama3.2:latest',label:'Llama 3.2 · más rápido'},{id:'llama3.1:8b',label:'Llama 3.1 8B'},{id:'gemma4:latest',label:'Gemma'}];copiado=signal(false);historial=signal<any[]>([]);private histKey='lunalav.pub.historial';iaLoading=signal(false);iaMsg=signal('');
  readonly temporadas:{id:string,label:string}[]=[{id:'ninguna',label:'Sin temporada'},{id:'navidad',label:'🎄 Navidad'},{id:'anionuevo',label:'🎆 Año Nuevo'},{id:'fiestaspatrias',label:'🇵🇪 Fiestas Patrias'},{id:'sanvalentin',label:'❤️ San Valentín'},{id:'diamadre',label:'💐 Día de la Madre'},{id:'blackfriday',label:'🖤 Black Friday'}];
  private readonly tempData:Record<string,{emoji:string,frase:string,tags:string[]}>={ninguna:{emoji:'',frase:'',tags:[]},navidad:{emoji:'🎄',frase:'En Navidad tu lavandería recibe más pedidos: contrólalos sin estrés.',tags:['#Navidad']},anionuevo:{emoji:'🎆',frase:'Empieza el año con tu lavandería ordenada y digital.',tags:['#AñoNuevo']},fiestaspatrias:{emoji:'🇵🇪',frase:'En Fiestas Patrias atiende más rápido y sin errores.',tags:['#FiestasPatrias','#Perú']},sanvalentin:{emoji:'❤️',frase:'San Valentín trae más ropa a lavar: no pierdas ningún pedido.',tags:['#SanValentín']},diamadre:{emoji:'💐',frase:'El Día de la Madre tu lavandería se llena: que no colapse.',tags:['#DíaDeLaMadre']},blackfriday:{emoji:'🖤',frase:'¡Black Friday! Digitaliza tu lavandería con una oferta especial.',tags:['#BlackFriday','#Ofertas']}};
  private readonly jobNames:Record<string,string>={MARKETING_SCORE_RECALCULATION:'Recalcular scores',MARKETING_FOLLOWUP_REVIEW:'Revisar seguimientos vencidos',MARKETING_PREPARE_OUTREACH:'Preparar contactos'};
  jobLabel(t:string){return this.jobNames[t]||t.replaceAll('_',' ');}
  loadAgentStatus(){this.svc.agentStatus().subscribe(x=>this.agentStatus.set(x));}
  runNow(type:string){this.svc.runAutomation(type).subscribe(()=>setTimeout(()=>this.loadAgentStatus(),1500));}
  ngOnDestroy(){if(this.timer)clearInterval(this.timer);}
  ngOnInit(){this.mode=this.route.snapshot.data['mode'];if(this.mode==='explorar')this.loadExplore();if(this.mode==='campanas')this.loadCampaigns();if(this.mode==='inbox')this.loadDrafts();if(this.mode==='calendario')this.svc.followups().subscribe(x=>this.followups.set(x));if(this.mode==='reportes')this.svc.dashboard().subscribe(x=>this.summary.set(x));if(this.mode==='agentes'){this.svc.agentSettings().subscribe(x=>this.agent.set(x));this.loadAgentStatus();this.timer=setInterval(()=>this.loadAgentStatus(),30000);}if(this.mode==='configuracion')this.svc.googleStatus().subscribe(x=>this.google.set(x));if(this.mode==='publicaciones'){this.generarPost();this.cargarHistorial();}}
  generarPost(){const e=this.pub.emojis;const n=(this.pub.negocio||'nuestra lavandería').trim();const o=(this.pub.oferta||'').trim();const z=(this.pub.zona||'').trim();const c=(this.pub.contacto||'').trim();const t=this.tempData[this.pub.temporada]||this.tempData['ninguna'];let cuerpo='';
    switch(this.pub.tipo){
      case 'promo':cuerpo=`${e?'🧺✨ ':''}¿Tienes una lavandería? Ordénala con ${n}.\n\nControla pedidos, clientes y cobros en un solo lugar${z?(' — '+z):''}. ${o||'Pídenos una demo gratis.'}`;break;
      case 'novedad':cuerpo=`${e?'🚀 ':''}Novedad en ${n}\n\n${o||'Nueva función para gestionar tu lavandería más fácil'}. El sistema que ordena tu negocio.`;break;
      case 'consejo':cuerpo=`${e?'💡 ':''}Tip para tu lavandería\n\n${o||'Lleva el control de cada pedido y cliente para no perder ventas ni prendas.'}\n\n${n} lo automatiza por ti.`;break;
      default:cuerpo=`${e?'⭐⭐⭐⭐⭐ ':''}"${o||('Desde que uso '+n+' controlo mi lavandería desde el celular y ya no pierdo pedidos.')}"\n\n— Dueño de lavandería que usa ${n}.`;
    }
    const temporada=t.frase?`${e&&t.emoji?t.emoji+' ':''}${t.frase}\n\n`:'';
    const cta=c?`\n\n${e?'📲 ':''}Escríbenos: ${c}`:'';
    this.postText=`${temporada}${cuerpo}${cta}\n\n${this.hashtags()}`.replace(/\n{3,}/g,'\n\n').trim();this.copiado.set(false);}
  private hashtags(){const t=['#Lavanderias','#GestiónDeLavandería',`#${(this.pub.negocio||'LunaLav').replace(/[^\p{L}\p{N}]/gu,'')||'LunaLav'}`];const z=(this.pub.zona||'').replace(/[^\p{L}\p{N}]/gu,'');if(z)t.push('#'+z);if(this.pub.tipo==='promo')t.push('#SoftwareParaLavanderías','#Demo');if(this.pub.tipo==='consejo')t.push('#Tips');t.push(...(this.tempData[this.pub.temporada]?.tags||[]));return [...new Set(t)].join(' ');}
  copiarPost(){const done=()=>{this.copiado.set(true);this.guardarHistorial();setTimeout(()=>this.copiado.set(false),2000);};if(navigator.clipboard?.writeText){navigator.clipboard.writeText(this.postText).then(done).catch(()=>this.fallbackCopy(done));}else this.fallbackCopy(done);}
  cargarHistorial(){try{const raw=localStorage.getItem(this.histKey);this.historial.set(raw?JSON.parse(raw):[]);}catch{this.historial.set([]);}}
  guardarHistorial(){const txt=this.postText.trim();if(!txt)return;const h=[{texto:txt,fecha:new Date().toISOString()},...this.historial().filter((x:any)=>x.texto!==txt)].slice(0,20);this.historial.set(h);try{localStorage.setItem(this.histKey,JSON.stringify(h));}catch{}}
  generarIA(){if(this.iaLoading())return;this.iaLoading.set(true);this.iaMsg.set('Generando con IA local (puede tardar unos segundos)…');this.svc.generarPublicacionIA({tipo:this.pub.tipo,temporada:this.pub.temporada,negocio:this.pub.negocio,oferta:this.pub.oferta,zona:this.pub.zona,contacto:this.pub.contacto,emojis:this.pub.emojis,modelo:this.pub.modelo}).subscribe({next:r=>{if(r?.texto)this.postText=r.texto;this.iaLoading.set(false);this.iaMsg.set('✔ Texto generado con IA. Puedes editarlo antes de publicar.');this.copiado.set(false);},error:e=>{this.iaLoading.set(false);this.iaMsg.set(e.error?.mensaje||'La IA local no está disponible ahora. Se mantiene el texto de plantilla.');}});}
  usarHistorial(x:any){this.postText=x.texto;this.copiado.set(false);}
  borrarHistorial(i:number){const h=this.historial().slice();h.splice(i,1);this.historial.set(h);try{localStorage.setItem(this.histKey,JSON.stringify(h));}catch{}}
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
