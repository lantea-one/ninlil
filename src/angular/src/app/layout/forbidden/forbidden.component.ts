import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  TextColorDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';

/**
 * This component is the `/forbidden` page that {@link permissionGuard} redirects to when the user
 * lacks a route's permission. It says so plainly and links back to the dashboard, so a user who
 * follows an old link or bookmark knows why the page did not open.
 */
@Component({
  selector: 'app-forbidden',
  imports: [
    RouterLink,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    TextColorDirective,
    IconDirective,
  ],
  template: `
    <c-card class="forbidden-card">
      <c-card-header class="d-flex align-items-center gap-2">
        <svg cIcon cTextColor="danger" name="cilBan" size="lg"></svg>
        <span>Access Denied</span>
      </c-card-header>
      <c-card-body>
        <p>Your account does not hold the permission required to view this page.</p>
      </c-card-body>
      <c-card-footer>
        <a cButton color="primary" routerLink="/dashboard">Back to Dashboard</a>
      </c-card-footer>
    </c-card>
  `,
  styles: `
    .forbidden-card {
      max-width: 480px;
      margin: 24px auto;
    }
  `,
})
export class ForbiddenComponent {}
