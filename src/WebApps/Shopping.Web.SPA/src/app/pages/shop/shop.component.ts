import { Component, inject, OnInit } from '@angular/core';
import { ShopService } from '../../core/services/shop.service';
import { Product } from '../../shared/models/product';
import { ProductItemComponent } from './product-item/product-item.component';

@Component({
  selector: 'app-shop',
  standalone: true,
  imports: [ProductItemComponent],
  templateUrl: './shop.component.html',
  styleUrl: './shop.component.scss',
})
export class ShopComponent implements OnInit {
  private readonly shopService = inject(ShopService);

  pageIndex = 1;
  pageSize = 10;
  products: Product[] = [];

  ngOnInit(): void {
    this.shopService
      .getProductPagination(this.pageIndex, this.pageSize)
      .subscribe({
        next: (response) => {
          this.products = response.items;
        },
        error: (error: any) => {
          console.log(error);
        },
      });
  }
}
