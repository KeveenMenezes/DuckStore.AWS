import { Component, inject, OnInit } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Product } from '../../shared/models/product';
import { ProductItemComponent } from './product-item/product-item.component';
import { CatalogService } from '../../core/services/catalog.service';
import { FiltersDialogComponent } from './filters-dialog/filters-dialog.component';
import { MatButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatMenu, MatMenuTrigger } from '@angular/material/menu';
import {
  MatListOption,
  MatSelectionList,
  MatSelectionListChange,
} from '@angular/material/list';

@Component({
  selector: 'app-shop',
  standalone: true,
  imports: [
    ProductItemComponent,
    MatButton,
    MatIcon,
    MatMenu,
    MatSelectionList,
    MatListOption,
    MatMenuTrigger,
  ],
  templateUrl: './shop.component.html',
  styleUrl: './shop.component.scss',
})
export class ShopComponent implements OnInit {
  private readonly catalogService = inject(CatalogService);
  private readonly matDialogService = inject(MatDialog);

  pageIndex = 1;
  pageSize = 10;
  products: Product[] = [];
  selectedBrands: string[] = [];
  selectedTypes: string[] = [];
  selectedSort: string = 'name';
  sortOptions = [
    { name: 'Alphabetical', value: 'name' },
    { name: 'Price: Low-High', value: 'priceAsc' },
    { name: 'Price: High-Low', value: 'priceDesc' },
  ];

  ngOnInit(): void {
    this.initializeShop();
  }

  initializeShop() {
    this.catalogService.getBrands();
    this.catalogService.getTypes();
    this.getProducts();
  }

  getProducts() {
    this.catalogService
      .getProducts(this.selectedBrands, this.selectedTypes, this.selectedSort)
      .subscribe({
        next: (response) => (this.products = response.items),
        error: (error) => console.error(error),
      });
  }

  //TODO: verificar se este metodo esta sendo usado.
  getProductsPagination() {
    this.catalogService
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

  onShortChange(event: MatSelectionListChange) {
    const selectedOption = event.options[0];
    if (selectedOption) {
      this.selectedSort = selectedOption.value;
      this.getProducts();
    }
  }

  openFiltersDialog() {
    const dialogRef = this.matDialogService.open(FiltersDialogComponent, {
      minWidth: '500px',
      data: {
        selectedBrands: this.selectedBrands,
        selectedTypes: this.selectedTypes,
      },
    });
    dialogRef.afterClosed().subscribe({
      next: (result) => {
        if (result) {
          console.log(result);
          this.selectedBrands = result.selectedBrands;
          this.selectedTypes = result.selectedTypes;
          this.getProducts();
        }
      },
    });
  }
}
