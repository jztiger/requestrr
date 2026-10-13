import { useEffect } from "react";
import { useSelector } from 'react-redux';
import BookshelfCategoryList from "./BookshelfCategoryList"
import { isCategoryValid } from "./validation"


// Top-level Bookshelf settings. There are no connection fields here: every category
// carries its own Bookshelf connection (e.g. one for audiobooks, one for ebooks).
function Bookshelf(props) {
  const reduxState = useSelector((state) => {
    return {
      settings: state.books.bookshelf,
      otherCategories: state.books.otherCategories
    }
  });

  const categories = reduxState.settings.categories;
  const isValid = categories.length > 0
    && categories.every(c => isCategoryValid(c, categories, reduxState.otherCategories));

  const { onValidate } = props;

  useEffect(() => {
    onValidate(isValid);
  }, [isValid]);


  return (
    <>
      <div>
        <h6 className="heading-small text-muted mb-4">
          Bookshelf Connection Settings
        </h6>
      </div>
      <div className="pl-lg-4">
        <p className="text-muted">
          Each category below has its own Bookshelf connection, quality profile, metadata profile and root folder.
          The category name becomes the slash command (for example <code>audiobook</code> or <code>ebook</code>).
        </p>
      </div>
      <BookshelfCategoryList isSubmitted={props.isSubmitted} isSaving={props.isSaving} />
    </>
  );
}

export default Bookshelf;
