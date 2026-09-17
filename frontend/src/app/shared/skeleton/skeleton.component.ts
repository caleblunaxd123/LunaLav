import { CommonModule } from '@angular/common';
import { Component, Input } from '@angular/core';

@Component({
  selector: 'app-skeleton',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './skeleton.component.html',
  styleUrl: './skeleton.component.scss'
})
export class SkeletonComponent {
  @Input() variant: 'table' | 'cards' | 'lines' = 'table';
  @Input() rows = 5;
  @Input() columns = 5;

  get filas(): number[] { return Array.from({ length: this.rows }, (_, i) => i); }
  get cols(): number[] { return Array.from({ length: this.columns }, (_, i) => i); }
}
