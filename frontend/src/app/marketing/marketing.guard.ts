import { inject } from '@angular/core'; import { CanActivateFn, Router } from '@angular/router'; import { MarketingAuthService } from './marketing-auth.service';
export const marketingGuard: CanActivateFn = () => { const a=inject(MarketingAuthService), r=inject(Router); return a.autenticado() || r.createUrlTree(['/marketing/login']); };
export const marketingGuestGuard: CanActivateFn = () => { const a=inject(MarketingAuthService), r=inject(Router); return !a.autenticado() || r.createUrlTree(['/marketing']); };
