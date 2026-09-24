import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConfirmDialogComponent, ConfirmDialogData } from './confirm-dialog.component';

describe('ConfirmDialogComponent', () => {
  let fixture: ComponentFixture<ConfirmDialogComponent>;

  async function createWith(data: ConfirmDialogData, visible = true): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [ConfirmDialogComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ConfirmDialogComponent);
    fixture.componentRef.setInput('data', data);
    fixture.componentRef.setInput('visible', visible);
    fixture.detectChanges();
  }

  it('renders the supplied title and message while visible', async () => {
    await createWith({ title: 'Delete zone?', message: 'This removes every record too.' });

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Delete zone?');
    expect(text).toContain('This removes every record too.');
  });

  it('defaults the confirm button label to "Delete" when none is supplied', async () => {
    await createWith({ title: 'Delete record?', message: 'This cannot be undone.' });

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Delete');
  });

  it('uses a supplied confirmLabel instead of the "Delete" default', async () => {
    await createWith({
      title: 'Disable zone?',
      message: 'Queries stop answering.',
      confirmLabel: 'Disable',
    });

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Disable');
  });

  it('onConfirm() emits resolved(true)', async () => {
    await createWith({ title: 'Delete zone?', message: 'This cannot be undone.' });
    const resolvedSpy = jasmine.createSpy('resolved');
    fixture.componentInstance.resolved.subscribe(resolvedSpy);

    fixture.componentInstance.onConfirm();

    expect(resolvedSpy).toHaveBeenCalledWith(true);
  });

  it('onCancel() emits resolved(false)', async () => {
    await createWith({ title: 'Delete zone?', message: 'This cannot be undone.' });
    const resolvedSpy = jasmine.createSpy('resolved');
    fixture.componentInstance.resolved.subscribe(resolvedSpy);

    fixture.componentInstance.onCancel();

    expect(resolvedSpy).toHaveBeenCalledWith(false);
  });

  it('onModalVisibleChange(false) emits resolved(false), matching a backdrop click or Escape', async () => {
    await createWith({ title: 'Delete zone?', message: 'This cannot be undone.' });
    const resolvedSpy = jasmine.createSpy('resolved');
    fixture.componentInstance.resolved.subscribe(resolvedSpy);

    fixture.componentInstance.onModalVisibleChange(false);

    expect(resolvedSpy).toHaveBeenCalledWith(false);
  });

  it('onModalVisibleChange(true) emits nothing -- only a transition to closed is a real outcome', async () => {
    await createWith({ title: 'Delete zone?', message: 'This cannot be undone.' });
    const resolvedSpy = jasmine.createSpy('resolved');
    fixture.componentInstance.resolved.subscribe(resolvedSpy);

    fixture.componentInstance.onModalVisibleChange(true);

    expect(resolvedSpy).not.toHaveBeenCalled();
  });
});
