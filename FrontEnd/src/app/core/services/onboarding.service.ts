import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

import {
  CompanyMembership,
  CompanyRole
} from '../models/auth.models';

export type CompanyIntent =
  | 'create_workspace'
  | 'join_workspace'
  | 'unsure';

export interface OnboardingAnswers {
  companyIntent: CompanyIntent;
  workSummary: string;
  decisionScope: string;
  teamScope: string;
}

export interface OnboardingRecommendation {
  recommendedRole: CompanyRole | 'NeedsReview';
  rawPrediction: CompanyRole | 'NeedsReview';
  needsClarification: boolean;
  clarificationQuestion: string | null;
  suggestedModules: string[];
  explanation: string;
  nextAction: 'CreateWorkspace' | 'RequestApproval' | 'Clarify';
  permissionEffect: 'recommendation_only';
  modelVersion: string;
}

export interface OnboardingReply {
  status: 'Completed' | 'NeedsClarification' | 'ApprovalNeeded';
  membership: CompanyMembership | null;
  recommendation: OnboardingRecommendation | null;
  message: string;
}

export interface OnboardingStatus {
  completed: boolean;
  hasResult: boolean;
}

export interface OnboardingInvitation {
  code: string;
  expiresAtUtc: string;
  allowedRoles: string[];
}
// Activitățile recunoscute de chestionarul structurat.
export type WorkFocus =
  | 'workspace_administration'
  | 'accounting'
  | 'project_coordination'
  | 'assigned_work'
  | 'unsure';

// Datele trimise pentru configurarea inițială.
export interface OnboardingSetupRequest {
  companyIntent: 'create_workspace' | 'join_workspace';
  companyName: string;
  invitationCode: string;
  confirmOwnership: boolean;
  acceptInvitation: boolean;
  workFocus: WorkFocus;
}

// Informații returnate numai după verificarea invitației.
export interface InvitationPreview {
  companyId: number;
  companyName: string;
  allowedRoles: Exclude<CompanyRole, 'Owner'>[];
  expiresAtUtc: string;
}
@Injectable({
  providedIn: 'root'
})
export class OnboardingService {
  private readonly http = inject(HttpClient);

  // Obținem un token CSRF pentru fiecare operație POST.
  private async post<T>(
    url: string,
    body: unknown
  ): Promise<T> {
    const csrf = await firstValueFrom(
      this.http.get<{ token: string }>(
        '/api/auth/csrf',
        { withCredentials: true }
      )
    );

    return firstValueFrom(
      this.http.post<T>(url, body, {
        withCredentials: true,
        headers: {
          'X-CSRF-TOKEN': csrf.token
        }
      })
    );
  }

  getStatus(): Promise<OnboardingStatus> {
    return firstValueFrom(
      this.http.get<OnboardingStatus>(
        '/api/onboarding/status',
        { withCredentials: true }
      )
    );
  }

  getResult(): Promise<CompanyMembership> {
    return firstValueFrom(
      this.http.get<CompanyMembership>(
        '/api/onboarding/result',
        { withCredentials: true }
      )
    );
  }

  recommend(
    answers: OnboardingAnswers
  ): Promise<OnboardingRecommendation> {
    return this.post(
      '/api/onboarding/recommend',
      answers
    );
  }

  // Backendul cere predicția și salvează atribuirea rolului.
  // Backendul validează alegerile și salvează apartenența.
   complete(data: OnboardingSetupRequest): Promise<OnboardingReply> {
    return this.post('/api/onboarding/complete', data);
  }

  // Verificarea invitației nu creează încă apartenența.
    previewInvitation(
    invitationCode: string
  ): Promise<InvitationPreview> {
    return this.post('/api/onboarding/invitation-preview', {
      invitationCode
    });
  }
  createInvitation(
    companyId: number,
    email: string,
    allowedRoles: string[]
  ): Promise<OnboardingInvitation> {
    return this.post(
      `/api/companies/${companyId}/onboarding-invitations`,
      {
        email,
        allowedRoles
      }
    );
  }
}