package com.soul.library.mvp;

/**
 * * @author soul
 *
 * @项目名:Compilations
 * @包名: com.soul.library.mvp
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/1/11 22:51
 */

public interface IPresenter<V extends IView> {

    /**
     * @param view 绑定
     */
    void attachView(V view);


    /**
     * 防止内存的泄漏,清楚presenter与activity之间的绑定
     */
    void detachView();


    /**
     * @return 获取View
     */
    IView getIView();
}
