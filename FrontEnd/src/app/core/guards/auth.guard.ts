import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from '../services/auth.service';

// Verificăm sesiunea inclusiv când utilizatorul reîncarcă pagina.
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  try {
    await auth.loadCurrentUser();
    return true;
  } catch {
    // Dacă sesiunea nu poate fi verificată, nu deschidem dashboardul.
    return router.createUrlTree(['/auth/login']);
  }
};