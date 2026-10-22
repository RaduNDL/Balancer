import { inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { CanActivateFn, Router } from '@angular/router';

import { OnboardingService } from '../services/onboarding.service';

// Ghidează conturile noi către chestionar înaintea paginilor de lucru.
// Autorizarea reală a operațiilor este verificată în backend.
export const onboardingGuard: CanActivateFn = async () => {
  const onboarding = inject(OnboardingService);
  const router = inject(Router);

  try {
    const status = await onboarding.getStatus();

    return status.completed
      ? true
      : router.createUrlTree(['/onboarding']);
  } catch (error) {
    return router.createUrlTree([
      error instanceof HttpErrorResponse && error.status === 401
        ? '/auth/login'
        : '/onboarding'
    ]);
  }
};