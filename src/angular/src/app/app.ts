import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NotificationHostComponent } from './shared/notification/notification-host.component';

/**
 * This is the root component: a `<router-outlet>` plus {@link NotificationHostComponent}, the
 * app-wide toast renderer. The router mounts the public pages (login, password reset, SSO
 * callback) and `AppShellComponent` directly, so this component has no layout. The toast host
 * lives here so it sits above every route, including the login page.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, NotificationHostComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
