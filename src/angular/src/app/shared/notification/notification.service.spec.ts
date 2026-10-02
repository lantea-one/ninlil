import { TestBed } from '@angular/core/testing';
import { NotificationService } from './notification.service';

/** These tests cover the toast queue: success and error delays, ordering and dismissal. */
describe('NotificationService', () => {
  let service: NotificationService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [NotificationService] });
    service = TestBed.inject(NotificationService);
  });

  it('success() queues a short, auto-dismissing toast', () => {
    service.success('Zone created.');

    expect(service.toasts()).toEqual([
      {
        id: jasmine.any(Number),
        message: 'Zone created.',
        color: 'success',
        autoHideDelayMs: 4000,
      },
    ]);
  });

  it('error() queues a toast with no auto-dismiss delay', () => {
    service.error('Save failed.');

    expect(service.toasts()[0].autoHideDelayMs).toBeNull();
    expect(service.toasts()[0].color).toBe('danger');
    expect(service.toasts()[0].message).toBe('Save failed.');
  });

  it('queues several toasts in the order they were shown', () => {
    service.success('First.');
    service.error('Second.');

    expect(service.toasts().map((toast) => toast.message)).toEqual(['First.', 'Second.']);
  });

  it('dismiss() removes exactly the toast with the given id, leaving the rest untouched', () => {
    service.success('First.');
    service.error('Second.');
    const [first, second] = service.toasts();

    service.dismiss(first.id);

    expect(service.toasts().length).toBe(1);
    expect(service.toasts()[0].id).toBe(second.id);
  });
});
