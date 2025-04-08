import { Component } from '@angular/core';
import { HeaderComponent } from './layout/header/header.component';
import { ShopComponent } from './pages/shop/shop.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [HeaderComponent, ShopComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {}
