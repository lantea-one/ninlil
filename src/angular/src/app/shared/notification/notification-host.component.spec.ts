import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NotificationHostComponent } from './notification-host.component';
import { NotificationService } from './notification.service';

describe('NotificationHostComponent', () => {
  let fixture: ComponentFixture<NotificationHostComponent>;
  let notification: NotificationService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NotificationHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(NotificationHostComponent);
    notification = TestBed.inject(NotificationService);
    fixture.detectChanges();
  });

  it('renders nothing when no toast is queued', () => {
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text.trim()).toBe('');
  });

  it('renders a success toast with its own message', () => {
    notification.success('Zone created.');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Zone created.');
    expect(text).toContain('Success');
  });

  it('renders an error toast with its own message', () => {
    notification.error('Save failed.');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Save failed.');
    expect(text).toContain('Error');
  });

  it('renders every currently-queued toast at once, stacked', () => {
    notification.success('First.');
    notification.error('Second.');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('First.');
    expect(text).toContain('Second.');
  });

  it('onToastVisibleChange(false, id) dismisses exactly that toast', () => {
    notification.success('First.');
    const [toast] = notification.toasts();

    fixture.componentInstance.onToastVisibleChange(false, toast.id);

    expect(notification.toasts().length).toBe(0);
  });

  it('onToastVisibleChange(true, id) does not dismiss the toast', () => {
    notification.success('First.');
    const [toast] = notification.toasts();

    fixture.componentInstance.onToastVisibleChange(true, toast.id);

    expect(notification.toasts().length).toBe(1);
  });
});
