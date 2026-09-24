import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NotificationHostComponent } from './shared/notification/notification-host.component';

/**
 * This is this app's root component -- a `<router-outlet>` host plus {@link
 * NotificationHostComponent}, the one, app-wide toast renderer. `LoginComponent` and
 * `AppShellComponent` are both mounted directly by the router (see `app.routes.ts`); neither
 * this component nor its own template hold any real page layout of their own -- the toast host
 * lives here specifically so it sits above every route, including the login page.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, NotificationHostComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
